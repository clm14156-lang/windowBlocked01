[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ApiBaseUrl = 'http://localhost:5080/'
)

$apiUri = $null
if (-not [Uri]::TryCreate($ApiBaseUrl, [UriKind]::Absolute, [ref]$apiUri) -or
    ($apiUri.Scheme -ne 'https' -and -not ($apiUri.Scheme -eq 'http' -and $apiUri.IsLoopback)) -or
    $apiUri.UserInfo -or $apiUri.Query -or $apiUri.Fragment)
{
    throw 'ApiBaseUrl must use HTTPS or loopback HTTP, without credentials, query, or fragment.'
}
$env:FOCUSTIME_API_BASE_URL = $apiUri.AbsoluteUri

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
$localDotnetRoot = Join-Path $repositoryRoot '.dotnet'
$localDotnetExecutable = Join-Path $localDotnetRoot 'dotnet.exe'
if (Test-Path -LiteralPath $localDotnetExecutable)
{
    $dotnetExecutable = $localDotnetExecutable
    $env:DOTNET_ROOT = $localDotnetRoot
    $env:PATH = "$localDotnetRoot;$env:PATH"
}
else
{
    $dotnetExecutable = 'dotnet'
}

$buildCacheRoot = Join-Path $repositoryRoot '.codex-build'
$env:NUGET_PACKAGES = Join-Path $buildCacheRoot 'nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $buildCacheRoot 'nuget-http'
$env:TEMP = Join-Path $buildCacheRoot 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path $buildCacheRoot 'dotnet-home'

@(
    $env:NUGET_PACKAGES,
    $env:NUGET_HTTP_CACHE_PATH,
    $env:TEMP,
    $env:DOTNET_CLI_HOME
) | ForEach-Object {
    New-Item -ItemType Directory -Path $_ -Force | Out-Null
}

& $dotnetExecutable restore FocusApp.sln --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0)
{
    Write-Error 'FocusApp package restore failed. No components were started.'
    exit $LASTEXITCODE
}

& $dotnetExecutable build FocusApp.sln --no-restore --configuration $Configuration
if ($LASTEXITCODE -ne 0)
{
    Write-Error 'FocusApp build failed. No components were started.'
    exit $LASTEXITCODE
}

$components = @('FocusApp.Service', 'FocusApp.Agent', 'FocusApp.Desktop')
foreach ($component in $components)
{
    $executable = Join-Path $repositoryRoot "src\$component\bin\$Configuration\net8.0-windows\$component.exe"
    if (-not (Test-Path -LiteralPath $executable))
    {
        throw "Built application was not found: $executable"
    }
    $windowStyle = if ($component -eq 'FocusApp.Desktop') { 'Normal' } else { 'Hidden' }
    Start-Process -FilePath $executable -WorkingDirectory $repositoryRoot -WindowStyle $windowStyle
}
