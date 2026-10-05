$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = '3.13'
$sha256 = 'BA63DFFC4410EE89193E1CB5A41989991BD77C61068DA17E3156D136B7B0B3D8'
$toolsDirectory = Join-Path $root '.tools'
$compiler = Join-Path $toolsDirectory "nsis-$version\makensis.exe"
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
$archive = Join-Path $toolsDirectory "nsis-$version.zip"
$url = "https://downloads.sourceforge.net/project/nsis/NSIS%203/$version/nsis-$version.zip"
Invoke-WebRequest -Uri $url -OutFile $archive -TimeoutSec 60
# SourceForge sometimes serves an HTML download page with a signed redirect.
$bytes = [IO.File]::ReadAllBytes($archive)
if ($bytes.Length -lt 2 -or $bytes[0] -ne 80 -or $bytes[1] -ne 75) {
    $page = [Text.Encoding]::UTF8.GetString($bytes)
    $link = [Net.WebUtility]::HtmlDecode([regex]::Match($page, 'data-release-url="([^"]+)"').Groups[1].Value)
    $redirect = [uri]$link
    if ($redirect.Scheme -ne 'https' -or $redirect.Host -ne 'downloads.sourceforge.net' -or $redirect.AbsolutePath -ne ([uri]$url).AbsolutePath) {
        throw 'NSIS download did not return the expected ZIP or SourceForge redirect.'
    }
    Invoke-WebRequest -Uri $redirect -OutFile $archive -TimeoutSec 60
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sha256) { throw 'NSIS download checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath $toolsDirectory -Force
if (!(Test-Path -LiteralPath $compiler)) { throw 'NSIS compiler missing from the verified download.' }
return $compiler
