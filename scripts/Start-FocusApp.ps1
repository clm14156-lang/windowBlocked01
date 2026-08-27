[CmdletBinding()]
param()

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runningApplications = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('FocusApp.Desktop.exe', 'FocusApp.Service.exe', 'FocusApp.Agent.exe') -and
    $_.ExecutablePath -like "$repositoryRoot\*"
}
if ($runningApplications)
{
    Write-Error 'FocusApp is already running. Run scripts\Stop-FocusApp.ps1 before starting it again.'
    exit 1
}

Set-Location -LiteralPath $repositoryRoot
dotnet build FocusApp.sln
if ($LASTEXITCODE -ne 0)
{
    Write-Error 'FocusApp build failed. No components were started.'
    exit $LASTEXITCODE
}

$components = @(
    @{ Title = 'FocusApp Service'; Project = 'src\FocusApp.Service\FocusApp.Service.csproj' },
    @{ Title = 'FocusApp Agent'; Project = 'src\FocusApp.Agent\FocusApp.Agent.csproj' },
    @{ Title = 'FocusApp Desktop'; Project = 'src\FocusApp.Desktop\FocusApp.Desktop.csproj' }
)

foreach ($component in $components)
{
    $command = @"
`$Host.UI.RawUI.WindowTitle = '$($component.Title)'
Set-Location -LiteralPath '$repositoryRoot'
dotnet run --no-build --project '$($component.Project)'
"@

    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoExit', '-Command', $command)
}
