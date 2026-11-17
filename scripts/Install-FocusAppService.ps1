#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallDirectory,
    [string]$PreviousBinaryPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FocusAppInstallDirectory.ps1')
if (!$InstallDirectory) { $InstallDirectory = Get-FocusAppServiceDirectory }
$installRoot = Get-FocusAppInstallPath $InstallDirectory
Assert-FocusAppInstallDirectory $installRoot

if (!(Test-Path -LiteralPath (Join-Path $installRoot 'FocusApp.Service.exe') -PathType Leaf)) {
    throw 'The FocusApp service installation is missing FocusApp.Service.exe.'
}

$serviceName = 'FocusAppService'
$binaryPath = '"{0}"' -f (Join-Path $installRoot 'FocusApp.Service.exe')
$registered = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($registered -and $registered.PathName -notin @($binaryPath, $PreviousBinaryPath)) {
    throw "The existing $serviceName service uses a different executable path."
}
if (-not $PSCmdlet.ShouldProcess($binaryPath, 'Configure and start FocusApp Windows service')) { return }

if ($registered) {
    if ($registered.PathName -ne $binaryPath) {
        $result = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{ PathName = $binaryPath }
        if ($result.ReturnValue -ne 0) { throw "Changing the FocusApp service executable failed: $($result.ReturnValue)." }
    }
    Set-Service -Name $serviceName -StartupType Automatic
} else {
    New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName 'FocusApp Service' -StartupType Automatic | Out-Null
}

& (Join-Path ([Environment]::SystemDirectory) 'sc.exe') failure $serviceName 'reset=' 86400 'actions=' 'restart/3000/restart/10000/restart/30000'
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Windows service recovery.' }

Start-Service -Name $serviceName
