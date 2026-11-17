[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.3',
    [string]$InnoCompiler,
    [uri]$ApiBaseUrl,
    [switch]$ReusePublish
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repository '.codex-build\installer'
$payload = Join-Path $work 'payload'
$servicePayload = Join-Path $work 'service-payload'
$installerHelpers = Join-Path $work 'installer-helpers'
$distribution = Join-Path $work 'dist'
$components = 'FocusApp.Desktop', 'FocusApp.Agent', 'FocusApp.Service'

if (!$InnoCompiler) {
    $InnoCompiler = @(
        (Join-Path $work 'tools\InnoSetup\ISCC.exe')
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 7 compiler not found. Pass its ISCC.exe path using -InnoCompiler.'
}
if ($ApiBaseUrl -and (!$ApiBaseUrl.IsAbsoluteUri -or $ApiBaseUrl.Scheme -ne 'https' -or
        $ApiBaseUrl.UserInfo -or $ApiBaseUrl.Query -or $ApiBaseUrl.Fragment -or !$ApiBaseUrl.AbsolutePath.EndsWith('/'))) {
    throw 'The release API URL must be absolute HTTPS, end in /, and contain no credentials, query or fragment.'
}

function Reset-OutputDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
        $workspace = (Resolve-Path -LiteralPath $repository).ProviderPath.TrimEnd('\')
        if (!$resolved.StartsWith($workspace + '\.codex-build\installer\', [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to clear an unexpected output directory: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

New-Item -ItemType Directory -Path $distribution -Force | Out-Null
Reset-OutputDirectory $payload
Reset-OutputDirectory $servicePayload
Reset-OutputDirectory $installerHelpers
$merged = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($component in $components) {
    $output = Join-Path $work "publish\$component"
    if (!$ReusePublish) {
        Reset-OutputDirectory $output
        $project = Join-Path $repository "src\$component\$component.csproj"
        & dotnet publish $project -c Release -r win-x64 --self-contained true -o $output `
            "-p:Version=$Version" -p:PublishSingleFile=false -p:PublishTrimmed=false -p:InstallerBuild=true
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $component." }
    }
    $executable = Get-Item -LiteralPath (Join-Path $output "$component.exe")
    if ($executable.PSIsContainer -or ($executable.VersionInfo.ProductVersion -split '\+', 2)[0] -ne $Version) {
        throw "Unexpected publish output version: $component.exe. Publish again without -ReusePublish."
    }
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = $file.FullName.Substring($output.Length + 1)
        if ($component -eq 'FocusApp.Service') {
            $destination = Join-Path $servicePayload $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
            continue
        }
        $destination = Join-Path $payload $relative
        if ($merged.ContainsKey($relative)) {
            if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
                throw "The publish outputs contain incompatible copies of $relative ($($merged[$relative]) and $component)."
            }
            continue
        }
        $merged.Add($relative, $component)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}

foreach ($required in 'hostfxr.dll', 'coreclr.dll', 'PresentationFramework.dll', 'e_sqlite3.dll', 'Assets\Audio\focus.mp3') {
    if (!$merged.ContainsKey($required)) { throw "Incomplete publish output: $required" }
}

foreach ($required in 'FocusApp.Service.exe', 'hostfxr.dll', 'coreclr.dll', 'e_sqlite3.dll') {
    if (!(Test-Path -LiteralPath (Join-Path $servicePayload $required) -PathType Leaf)) { throw "Incomplete service publish output: $required" }
}

# Elevated maintenance always runs from the protected service directory or the packaged installer.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InstallerLifecycle.ps1'), (Join-Path $PSScriptRoot 'Launch-FocusApp.ps1'),
    (Join-Path $repository 'scripts\FocusAppInstallDirectory.ps1') -Destination $installerHelpers
$logo = Join-Path $repository 'src\FocusApp.Desktop\Assets\Icons\Common\clock_icon.png'
$icon = Join-Path $payload 'FocusTime.ico'
# The existing artwork is a 256 × 256 PNG. Windows ICO supports a PNG image as its payload.
$png = [IO.File]::ReadAllBytes($logo)
$writer = [IO.BinaryWriter]::new([IO.File]::Create($icon))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]1)
    $writer.Write([byte[]]@(0, 0, 0, 0))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$png.Length)
    $writer.Write([uint32]22)
    $writer.Write([byte[]]$png)
} finally { $writer.Dispose() }
$releaseFile = 'FocusTime.ReleaseVersion.txt'
Set-Content -LiteralPath (Join-Path $payload $releaseFile) -Value $Version -Encoding ASCII
$installedFiles = @($merged.Keys) + @($releaseFile, 'FocusTime.ico')
if ($ApiBaseUrl) {
    Set-Content -LiteralPath (Join-Path $payload 'FocusTime.ApiBaseUrl.txt') -Value $ApiBaseUrl.AbsoluteUri -Encoding UTF8
    $installedFiles += 'FocusTime.ApiBaseUrl.txt'
} else {
    Write-Warning 'No HTTPS API URL is configured. This package uses the existing local development endpoint.'
}
$manifest = 'FocusTime.InstallManifest.txt'
Set-Content -LiteralPath (Join-Path $payload $manifest) -Value ($installedFiles | Sort-Object) -Encoding UTF8
$serviceFiles = Get-ChildItem -LiteralPath $servicePayload -Recurse -File | ForEach-Object { $_.FullName.Substring($servicePayload.Length + 1) }
Set-Content -LiteralPath (Join-Path $servicePayload $manifest) -Value ($serviceFiles | Sort-Object) -Encoding UTF8
$serviceArchive = Join-Path $work 'FocusTime.ServicePayload.zip'
if (Test-Path -LiteralPath $serviceArchive) { Remove-Item -LiteralPath $serviceArchive }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($servicePayload, $serviceArchive)

$script = Join-Path $PSScriptRoot 'FocusTimeSetup.iss'
& $InnoCompiler /Q "/DAppReleaseVersion=$Version" "/DPayloadDir=$payload" "/DInstallerHelpersDir=$installerHelpers" "/DServiceArchive=$serviceArchive" "/DDistributionDir=$distribution" `
    "/DAppLogo=$logo" "/DAppIcon=$icon" "/DRegisterServiceScript=$(Join-Path $repository 'scripts\Install-FocusAppService.ps1')" $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setup = Join-Path $distribution "FocusTime-Setup-$Version-win-x64.exe"
Get-Item -LiteralPath $setup
Get-FileHash -LiteralPath $setup -Algorithm SHA256
