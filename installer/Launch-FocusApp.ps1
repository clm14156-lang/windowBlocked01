param([Parameter(Mandatory)][string]$InstallDirectory)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 1 }
    Start-Process -FilePath (Join-Path $InstallDirectory 'FocusApp.Agent.exe') -WorkingDirectory $InstallDirectory -ArgumentList '--launch-after-setup'
} finally { $identity.Dispose() }
