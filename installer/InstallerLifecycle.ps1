#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Plan', 'Prepare', 'Verify', 'Install', 'Commit', 'Rollback', 'CheckUninstall', 'Uninstall')]
    [string]$Action,
    [Parameter(Mandatory)]
    [string]$InstallDirectory,
    [Parameter(Mandatory)]
    [string]$TransactionDirectory,
    [version]$ReleaseVersion,
    [string]$ServiceScript,
    [string]$PayloadManifest,
    [string]$ServiceArchive
)

$ErrorActionPreference = 'Stop'
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$serviceName = 'FocusAppService'
$manifestName = 'FocusTime.InstallManifest.txt'
$versionName = 'FocusTime.ReleaseVersion.txt'
$registryPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{9ED4ACF8-F19E-4DFD-863D-38E7CA3DEA62}_is1'
. (Join-Path $PSScriptRoot 'FocusAppInstallDirectory.ps1')

$journal = Join-Path $TransactionDirectory 'state.clixml'
$clientBackup = Join-Path $TransactionDirectory 'client'
$serviceBackup = Join-Path $TransactionDirectory 'service'
$stagedService = Join-Path $TransactionDirectory 'service-staging'

function Get-OwnedService {
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    if ($service -and $service.PathName -notin @($servicePath, $legacyServicePath)) {
        throw "FOCUSTIME_FOREIGN_SERVICE: $($service.PathName)"
    }
    $service
}

function Assert-ClientsClosed {
    if (Get-Process -Name 'FocusApp.Desktop', 'FocusApp.Agent' -ErrorAction SilentlyContinue) {
        throw 'FOCUSTIME_CLIENTS_RUNNING'
    }
}

function Stop-OwnedService {
    $service = Get-OwnedService
    if ($service -and $service.State -ne 'Stopped') {
        $controller = Get-Service -Name $serviceName
        try {
            if ($controller.Status -ne 'StopPending') { $controller.Stop() }
            $controller.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(25))
        } finally { $controller.Dispose() }
    }
}

function Remove-OwnedService {
    if (!(Get-OwnedService)) { return }
    Stop-OwnedService
    & (Join-Path ([Environment]::SystemDirectory) 'sc.exe') delete $serviceName | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not remove the FocusApp Windows service.' }
}

function Get-PayloadFiles([string]$Manifest) {
    foreach ($file in Get-Content -LiteralPath $Manifest -Encoding UTF8) {
        if ($file -notmatch '^[^:\/]+(?:\\[^:\/]+)*$' -or $file -match '(?:^|\\)\.\.?(?:\\|$)') {
            throw 'The installed file manifest is invalid.'
        }
        $file
    }
}

function Copy-InstallTree([string]$Source, [string]$Destination) {
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    & (Join-Path ([Environment]::SystemDirectory) 'robocopy.exe') $Source $Destination /E /COPY:DAT /XJ /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "Could not copy recovery files: $Source" }
}

function Remove-PayloadFiles([string]$Root, [string[]]$Files) {
    $parents = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Files) {
        $path = Join-Path $Root $file
        if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        $parent = [IO.Path]::GetDirectoryName($path)
        while ($parent -ne $Root -and $parents.Add($parent)) { $parent = [IO.Path]::GetDirectoryName($parent) }
    }
    foreach ($parent in $parents | Sort-Object { $_.Length } -Descending) {
        if ([IO.Directory]::Exists($parent) -and !(Get-ChildItem -LiteralPath $parent -Force | Select-Object -First 1)) {
            [IO.Directory]::Delete($parent)
        }
    }
}

function Confirm-ServiceReady {
    $service = Get-OwnedService
    if (!$service -or $service.PathName -ne $servicePath -or $service.State -ne 'Running') {
        throw 'FocusAppService did not start from the protected service directory.'
    }
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'FocusApp.Service.v2', [IO.Pipes.PipeDirection]::InOut)
    try { $pipe.Connect(6000) } finally { $pipe.Dispose() }
}

function Remove-StartupEntries {
    $agent = Join-Path $installRoot 'FocusApp.Agent.exe'
    foreach ($profile in Get-ChildItem -LiteralPath 'Registry::HKEY_USERS') {
        if ($profile.PSChildName -notmatch '^S-1-(?:5-21|12-1)-[\d-]+$') { continue }
        $key = Join-Path $profile.PSPath 'Software\Microsoft\Windows\CurrentVersion\Run'
        $entry = Get-ItemPropertyValue -LiteralPath $key -Name 'FocusApp' -ErrorAction SilentlyContinue
        if ($entry -in @(('"' + $agent + '" --autostart'), ('"' + $agent + '"'), $agent)) {
            Remove-ItemProperty -LiteralPath $key -Name 'FocusApp'
        }
    }
}

function Assert-InstallPathLength([string]$Root, [string]$Manifest) {
    foreach ($file in Get-PayloadFiles $Manifest) {
        $path = Join-Path $Root $file
        if ($path.Length -ge 260 -or [IO.Path]::GetDirectoryName($path).Length -ge 248) {
            throw "FOCUSTIME_TOO_LONG: $Root"
        }
    }
}

function Stop-AndRestoreService($State) {
    if (!$State.HadService) {
        Remove-OwnedService
        return
    }
    Stop-OwnedService
    $service = Get-OwnedService
    if ($service.PathName -ne $State.PreviousServicePath) {
        $result = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = $State.PreviousServicePath }
        if ($result.ReturnValue -ne 0) { throw "Restoring the FocusApp service executable failed: $($result.ReturnValue)." }
    }
}

try {
    if ($Action -eq 'Rollback' -and ![IO.File]::Exists($journal)) { exit 0 }
    $installRoot = Get-FocusAppInstallPath $InstallDirectory
    $serviceRoot = Get-FocusAppInstallPath (Get-FocusAppServiceDirectory)
    $servicePath = '"' + (Join-Path $serviceRoot 'FocusApp.Service.exe') + '"'
    $legacyServicePath = $null
    if ($Action -in 'Install', 'Commit', 'Rollback') {
        $state = Import-Clixml -LiteralPath $journal
        if ($state.Root -ne $installRoot) { throw 'Installation transaction directory mismatch.' }
        $legacyServicePath = $state.PreviousServicePath
    }
    switch ($Action) {
        'Plan' {
            $registration = Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue
            $installedPath = $registration.'Inno Setup: App Path'
            if ($installedPath) {
                if ($installRoot -ne $installedPath.TrimEnd('\')) { throw "FOCUSTIME_RELOCATION: $installedPath" }
                [Console]::WriteLine('FOCUSTIME_UPGRADE')
            } elseif ((Test-FocusAppDirectoryPath $installRoot) -and
                (Get-ChildItem -LiteralPath $installRoot -Force | Select-Object -First 1)) {
                # Use a dedicated child in an occupied folder so that no pre-existing file can be overwritten.
                $installRoot = Join-Path $installRoot 'FocusApp'
            }
            if ($installRoot -eq $serviceRoot -or
                $installRoot.StartsWith($serviceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "FOCUSTIME_RESERVED: $installRoot"
            }
            $null = Test-FocusAppDirectoryPath $installRoot
            Assert-InstallPathLength $installRoot $PayloadManifest
            [Console]::WriteLine('FOCUSTIME_TARGET: ' + $installRoot)
        }
        'Prepare' {
            Assert-ClientsClosed
            $registration = Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue
            $installedPath = $registration.'Inno Setup: App Path'
            $upgrade = [bool]$installedPath
            $existing = Test-FocusAppDirectoryPath $installRoot
            $clientManifest = Join-Path $installRoot $manifestName
            $legacyUninstaller = $null

            if ($upgrade) {
                if ($installRoot -ne $installedPath.TrimEnd('\')) { throw "FOCUSTIME_RELOCATION: $installedPath" }
                Assert-FocusAppInstallDirectory $installRoot -Client
                $installedVersion = [version](Get-Content -Raw -LiteralPath (Join-Path $installRoot $versionName)).Trim()
                if ($ReleaseVersion -lt $installedVersion) { throw "FOCUSTIME_DOWNGRADE: $installedVersion" }
                if ($installedVersion -lt [version]'0.1.3') {
                    # Only a registered legacy installation can authorize migrating an existing service.
                    Assert-FocusAppInstallDirectory $installRoot
                    $legacyServicePath = '"' + (Join-Path $installRoot 'FocusApp.Service.exe') + '"'
                    $previousUninstaller = ([string]$registration.UninstallString).Trim('"')
                    if ([IO.Path]::GetDirectoryName($previousUninstaller) -eq $installRoot -and
                        [IO.Path]::GetFileName($previousUninstaller) -match '^unins\d+\.exe$') {
                        $legacyUninstaller = $previousUninstaller
                    }
                }
            } elseif ($existing -and (Get-ChildItem -LiteralPath $installRoot -Force | Select-Object -First 1)) {
                throw "FOCUSTIME_OCCUPIED_PATH: $installRoot"
            }

            Assert-FocusAppInstallParent $serviceRoot
            $serviceDirectoryExists = Test-FocusAppDirectoryPath $serviceRoot
            if ($serviceDirectoryExists) {
                Assert-FocusAppInstallDirectory $serviceRoot
                if (!$upgrade -and (Get-ChildItem -LiteralPath $serviceRoot -Force | Select-Object -First 1)) {
                    throw "FOCUSTIME_OCCUPIED_PATH: $serviceRoot"
                }
            }
            $service = Get-OwnedService
            if ($service -and !$upgrade) { throw "FOCUSTIME_FOREIGN_SERVICE: $($service.PathName)" }

            New-Item -ItemType Directory -Path $TransactionDirectory -Force | Out-Null
            $previousFiles = @()
            $previousServiceFiles = @()
            if ($upgrade) {
                $previousFiles = @(Get-PayloadFiles $clientManifest)
                Copy-InstallTree $installRoot $clientBackup
                if ($serviceDirectoryExists) {
                    $serviceManifest = Join-Path $serviceRoot $manifestName
                    if ([IO.File]::Exists($serviceManifest)) { $previousServiceFiles = @(Get-PayloadFiles $serviceManifest) }
                    Copy-InstallTree $serviceRoot $serviceBackup
                }
            }
            $state = [pscustomobject]@{
                Root = $installRoot
                Upgrade = $upgrade
                HadService = [bool]$service
                WasRunning = $service -and $service.State -eq 'Running'
                StartMode = if ($service) { $service.StartMode } else { '' }
                PreviousServicePath = if ($service) { $service.PathName } else { $null }
                PreviousFiles = $previousFiles
                PreviousServiceFiles = $previousServiceFiles
                LegacyUninstaller = $legacyUninstaller
                CreatedClient = $false
                CreatedServiceDirectory = $false
                OriginalClientSecurity = if ($existing -and !$upgrade) {
                    (Get-Acl -LiteralPath $installRoot).GetSecurityDescriptorBinaryForm()
                } else { $null }
            }
            $state | Export-Clixml -LiteralPath $journal
            if (!$upgrade) {
                if ($existing) { [IO.Directory]::Delete($installRoot) }
                [IO.Directory]::CreateDirectory($installRoot, (Get-FocusAppProtectedDirectorySecurity)) | Out-Null
                $state.CreatedClient = $true
                $state | Export-Clixml -LiteralPath $journal
            }
            if (!$serviceDirectoryExists) {
                [IO.Directory]::CreateDirectory($serviceRoot, (Get-FocusAppProtectedDirectorySecurity)) | Out-Null
                $state.CreatedServiceDirectory = $true
                $state | Export-Clixml -LiteralPath $journal
            }
            Stop-OwnedService
        }
        'Verify' {
            Assert-FocusAppInstallDirectory $installRoot -Client
            Assert-FocusAppInstallDirectory $serviceRoot
        }
        'Install' {
            Assert-FocusAppInstallDirectory $installRoot -Client
            Assert-FocusAppInstallDirectory $serviceRoot
            try {
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                [IO.Compression.ZipFile]::ExtractToDirectory($ServiceArchive, $stagedService)
                Copy-InstallTree $stagedService $serviceRoot
                & $ServiceScript -InstallDirectory $serviceRoot -PreviousBinaryPath $legacyServicePath
                Confirm-ServiceReady
            } catch {
                $failure = $_
                try { Stop-OwnedService } catch { Write-Output "Could not stop service: $($_.Exception.Message)" }
                throw $failure
            }
        }
        'Commit' {
            Assert-FocusAppInstallDirectory $installRoot -Client
            Assert-FocusAppInstallDirectory $serviceRoot
            foreach ($part in @(
                @{ Root = $installRoot; Before = $state.PreviousFiles }
                @{ Root = $serviceRoot; Before = $state.PreviousServiceFiles }
            )) {
                $current = @(Get-PayloadFiles (Join-Path $part.Root $manifestName))
                Remove-PayloadFiles $part.Root @($part.Before | Where-Object { $_ -notin $current })
            }
            if ($state.LegacyUninstaller) {
                foreach ($extension in '.exe', '.dat', '.msg') {
                    [IO.File]::Delete([IO.Path]::ChangeExtension($state.LegacyUninstaller, $extension))
                }
            }
        }
        'Rollback' {
            Stop-AndRestoreService $state
            $stagedManifest = Join-Path $stagedService $manifestName
            if ([IO.File]::Exists($stagedManifest)) {
                Assert-FocusAppInstallDirectory $serviceRoot
                $newServiceFiles = @(Get-PayloadFiles $stagedManifest | Where-Object { $_ -notin $state.PreviousServiceFiles })
                if (![IO.Directory]::Exists($serviceBackup)) { $newServiceFiles += $manifestName }
                Remove-PayloadFiles $serviceRoot $newServiceFiles
            }
            if ($state.Upgrade) {
                Assert-FocusAppInstallDirectory $installRoot -Client
                Copy-InstallTree $clientBackup $installRoot
                if ([IO.Directory]::Exists($serviceBackup)) {
                    Assert-FocusAppInstallDirectory $serviceRoot
                    Copy-InstallTree $serviceBackup $serviceRoot
                }
            } elseif ($state.CreatedClient -and (Test-FocusAppDirectoryPath $installRoot) -and
                !(Get-ChildItem -LiteralPath $installRoot -Force | Select-Object -First 1)) {
                Assert-FocusAppInstallDirectory $installRoot -Client
                [IO.Directory]::Delete($installRoot)
            }
            if ($state.OriginalClientSecurity -and !(Test-FocusAppDirectoryPath $installRoot)) {
                $security = [Security.AccessControl.DirectorySecurity]::new()
                $security.SetSecurityDescriptorBinaryForm([byte[]]$state.OriginalClientSecurity)
                [IO.Directory]::CreateDirectory($installRoot, $security) | Out-Null
            }
            if ($state.CreatedServiceDirectory -and (Test-FocusAppDirectoryPath $serviceRoot) -and
                !(Get-ChildItem -LiteralPath $serviceRoot -Force | Select-Object -First 1)) {
                Assert-FocusAppInstallDirectory $serviceRoot
                [IO.Directory]::Delete($serviceRoot)
            }
            if ($state.HadService) {
                $startMode = if ($state.StartMode -eq 'Auto') { 'Automatic' } else { $state.StartMode }
                Set-Service -Name $serviceName -StartupType $startMode
                if ($state.WasRunning) { Start-Service -Name $serviceName }
            }
        }
        'CheckUninstall' {
            Assert-FocusAppInstallDirectory $serviceRoot
            if (Test-FocusAppDirectoryPath $installRoot) { Assert-FocusAppInstallDirectory $installRoot -Client }
            Assert-ClientsClosed
            $null = Get-OwnedService
        }
        'Uninstall' {
            Assert-FocusAppInstallDirectory $serviceRoot
            Remove-OwnedService
            Remove-StartupEntries
            $serviceManifest = Join-Path $serviceRoot $manifestName
            if ([IO.File]::Exists($serviceManifest)) {
                Remove-PayloadFiles $serviceRoot (@(Get-PayloadFiles $serviceManifest) + $manifestName)
            }
        }
    }
} catch [UnauthorizedAccessException] {
    [Console]::WriteLine('FOCUSTIME_ERROR: FOCUSTIME_ACCESS: ' + $_.Exception.Message)
    exit 1
} catch {
    [Console]::WriteLine('FOCUSTIME_ERROR: ' + $_.Exception.Message)
    exit 1
}
