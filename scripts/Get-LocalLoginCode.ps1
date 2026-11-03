[CmdletBinding()]
param([string]$Email)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$mailDirectory = Join-Path $repositoryRoot '.codex-build\local-login-server\api\.artifacts\mail'
if (-not (Test-Path -LiteralPath $mailDirectory)) {
    throw 'No development mail directory exists. Start the local API and request a code first.'
}
foreach ($file in (Get-ChildItem -LiteralPath $mailDirectory -Filter '*.eml' | Sort-Object LastWriteTimeUtc -Descending)) {
    $message = Get-Content -Raw -LiteralPath $file.FullName
    $separator = $message.IndexOf("`r`n`r`n")
    if ($separator -lt 0) { continue }
    $headers = $message.Substring(0, $separator)
    $recipient = [regex]::Match($headers, '(?im)^To:\s*([^\r\n]+)').Groups[1].Value.Trim()
    if ($Email -and -not [string]::Equals($recipient, $Email.Trim(), [StringComparison]::OrdinalIgnoreCase)) { continue }
    $body = $message.Substring($separator + 4).Trim()
    if ($headers -match '(?im)^Content-Transfer-Encoding:\s*base64') {
        $body = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($body))
    }
    $code = [regex]::Match($body, '[0-9]{6}').Value
    if (-not $code) { continue }
    Write-Output "Email: $recipient"
    Write-Output "Code: $code"
    Write-Output "Created: $($file.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) (valid for 10 minutes, one use)"
    return
}
throw 'No matching verification mail was found. Request a code in the client first.'
