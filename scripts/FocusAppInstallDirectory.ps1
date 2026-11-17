function Get-FocusAppProgramFilesDirectory {
    $windows = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $windows.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion')
        try { [IO.Path]::GetFullPath($key.GetValue('ProgramFilesDir')).TrimEnd('\') }
        finally { $key.Dispose() }
    } finally { $windows.Dispose() }
}

function Get-FocusAppServiceDirectory {
    Join-Path (Get-FocusAppProgramFilesDirectory) 'FocusApp Service'
}

function Get-FocusAppInstallVolume([string]$Root) {
    $drive = [IO.DriveInfo]::new($Root)
    if (!$drive.IsReady -or $drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable)) { return }
    $letter = $Root.TrimEnd('\')
    if (Get-CimInstance Win32_Volume -Filter "DriveLetter='$letter'") { $drive.DriveFormat }
}

function Get-FocusAppInstallPath([string]$Path) {
    $Path = $Path.TrimEnd('\')
    if ($Path -notmatch '^[A-Za-z]:\\[^\\]+(?:\\[^\\]+)*$') { throw "FOCUSTIME_PATH: $Path" }
    foreach ($part in $Path.Substring(3).Split('\')) {
        if ($part -match '[\x00-\x1f<>:"/|?*]|[ .]$' -or
            $part -match '^(CON|PRN|AUX|NUL|COM[1-9\u00b9\u00b2\u00b3]|LPT[1-9\u00b9\u00b2\u00b3]) *(\.|$)') { throw "FOCUSTIME_PATH: $Path" }
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($fullPath)
    $format = Get-FocusAppInstallVolume $root
    if (!$format) { throw "FOCUSTIME_VOLUME: $root" }
    if ($format -notin 'NTFS', 'ReFS') { throw "FOCUSTIME_FILESYSTEM: $root ($format)" }
    $fullPath
}

function Assert-FocusAppPlainDirectory([string]$Path) {
    try { $attributes = [IO.File]::GetAttributes($Path) }
    catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] { return $false }
    if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "FOCUSTIME_REPARSE: $Path" }
    if (!($attributes -band [IO.FileAttributes]::Directory)) { throw "FOCUSTIME_NOT_DIRECTORY: $Path" }
    $true
}

function Test-FocusAppDirectoryPath([string]$Path) {
    $current = [IO.Path]::GetPathRoot($Path)
    foreach ($part in $Path.Substring($current.Length).Split('\')) {
        $current = Join-Path $current $part
        if (!(Assert-FocusAppPlainDirectory $current)) { return $false }
    }
    $true
}

function Get-FocusAppProtectedDirectorySecurity {
    $administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetOwner($administrators)
    $security.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($entry in @(
        @{ Sid = 'S-1-5-32-544'; Rights = 'FullControl' }
        @{ Sid = 'S-1-5-18'; Rights = 'FullControl' }
        @{ Sid = 'S-1-5-32-545'; Rights = 'ReadAndExecute' }
    )) {
        $sid = [Security.Principal.SecurityIdentifier]::new($entry.Sid)
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $sid, [Security.AccessControl.FileSystemRights]$entry.Rights, $inherit, 'None', 'Allow'))
    }
    $security
}

function Assert-FocusAppProtectedItem($Item, [switch]$VolumeRoot) {
    if ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "FOCUSTIME_REPARSE: $($Item.FullName)"
    }
    $acl = Get-Acl -LiteralPath $Item.FullName
    $trusted = 'S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
        throw "FOCUSTIME_UNSAFE_PATH: $($Item.FullName)"
    }
    $rights = [int][Security.AccessControl.FileSystemRights]'DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    # The drive root cannot be renamed. Check rights that can replace a child directory.
    $rights = $rights -bor 0x10000000
    if (!$VolumeRoot) {
        $rights = $rights -bor 0x40000000 -bor [int][Security.AccessControl.FileSystemRights]'Write, Delete'
    }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and ([int]$rule.FileSystemRights -band $rights) -and
            !($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and
            $rule.IdentityReference.Value -notin $trusted) {
            throw "FOCUSTIME_UNSAFE_PATH: $($Item.FullName)"
        }
    }
}

function Assert-FocusAppInstallParent([string]$Path) {
    $root = [IO.Path]::GetPathRoot($Path)
    $ancestors = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
    $parent = [IO.Directory]::GetParent($Path)
    while ($parent) {
        $ancestors.Push($parent)
        $parent = $parent.Parent
    }
    while ($ancestors.Count -gt 0) {
        $parent = $ancestors.Pop()
        if (!$parent.Exists) { throw "FOCUSTIME_MISSING_PARENT: $($parent.FullName)" }
        Assert-FocusAppProtectedItem $parent -VolumeRoot:($parent.FullName -eq $root)
    }
}

function Assert-FocusAppInstallDirectory([string]$Path, [switch]$Client) {
    # The installer pins the client directory chain for the duration of elevated writes.
    if (!$Client) { Assert-FocusAppInstallParent $Path }
    $root = Get-Item -LiteralPath $Path -Force
    if (!$root.PSIsContainer) { throw "FOCUSTIME_PATH: $Path" }
    Assert-FocusAppProtectedItem $root
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($root.FullName)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath ($pending.Dequeue()) -Force) {
            Assert-FocusAppProtectedItem $item
            if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
        }
    }
}
