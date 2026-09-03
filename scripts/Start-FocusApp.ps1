[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runningApplications = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('FocusApp.Desktop.exe', 'FocusApp.Service.exe', 'FocusApp.Agent.exe')
}
if ($runningApplications)
{
    $runningPaths = $runningApplications | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_.ExecutablePath)) { $_.Name } else { $_.ExecutablePath }
    }
    Write-Error ("FocusApp is already running. Run scripts\Stop-FocusApp.ps1 before starting it again.`n" +
        ($runningPaths -join "`n"))
    exit 1
}

Set-Location -LiteralPath $repositoryRoot
$workspaceDriveRoot = [System.IO.Path]::GetPathRoot($repositoryRoot)
$env:NUGET_PACKAGES = Join-Path $workspaceDriveRoot '.focusapp-nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $workspaceDriveRoot '.focusapp-nuget-http'
$env:TEMP = Join-Path $workspaceDriveRoot '.focusapp-temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $workspaceDriveRoot '.focusapp-dotnet'

@(
    $env:NUGET_PACKAGES,
    $env:NUGET_HTTP_CACHE_PATH,
    $env:TEMP,
    $env:DOTNET_CLI_HOME
) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
}

dotnet restore FocusApp.sln --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0)
{
    Write-Error 'FocusApp package restore failed. No components were started.'
    exit $LASTEXITCODE
}

dotnet build FocusApp.sln --no-restore --configuration $Configuration
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
dotnet run --no-build --configuration '$Configuration' --project '$($component.Project)'
"@

    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoExit', '-Command', $command)
}
