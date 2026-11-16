#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'FocusApp')
)

$ErrorActionPreference = 'Stop'
$installRoot = (Resolve-Path -LiteralPath $InstallDirectory).ProviderPath
$programFiles = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\') + '\'
if (-not $installRoot.StartsWith($programFiles, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Install the trusted FocusApp release in an administrator-managed folder under Program Files first.'
}

foreach ($name in @('FocusApp.Service.exe', 'FocusApp.Agent.exe', 'FocusApp.Desktop.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot $name) -PathType Leaf)) {
        throw "The FocusApp installation is missing $name."
    }
}

$installItem = Get-Item -LiteralPath $installRoot
for ($directory = $installItem; $directory.FullName -ne $programFiles.TrimEnd('\'); $directory = $directory.Parent) {
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The FocusApp installation path contains a junction or symbolic link: $($directory.FullName)"
    }
}
if (Get-ChildItem -LiteralPath $installRoot -Force -Recurse -Attributes ReparsePoint | Select-Object -First 1) {
    throw 'The FocusApp installation contains a junction or symbolic link.'
}

$serviceName = 'FocusAppService'
$binaryPath = '"{0}"' -f (Join-Path $installRoot 'FocusApp.Service.exe')
$registered = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($registered -and $registered.PathName -ne $binaryPath) {
    throw "The existing $serviceName service uses a different executable path."
}
if (-not $PSCmdlet.ShouldProcess($binaryPath, 'Configure and start FocusApp Windows service')) { return }

if ($registered) {
    Set-Service -Name $serviceName -StartupType Automatic
} else {
    New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName 'FocusApp Service' -StartupType Automatic | Out-Null
}

& sc.exe failure $serviceName 'reset=' 86400 'actions=' 'restart/3000/restart/10000/restart/30000'
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Windows service recovery.' }

Start-Service -Name $serviceName
