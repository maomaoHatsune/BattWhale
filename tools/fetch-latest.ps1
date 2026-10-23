# Downloads the newest BattWhale.exe from GitHub Releases and clears the
# Mark-of-the-Web, so SmartScreen does not stop it on first launch.
#
# Why this exists: SmartScreen only intervenes on files carrying a zone stream
# (":Zone.Identifier"), which browsers and Explorer attach to anything fetched
# from the internet. A locally built exe never has one. Downloading through
# Invoke-WebRequest instead of a browser leaves no such stream, and Unblock-File
# below removes it if a proxy or a future PowerShell adds one anyway.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI,
# so any Chinese literal here would come out as mojibake.

param(
    [string]$Out = (Join-Path (Get-Location) 'BattWhale.exe')
)

$ErrorActionPreference = 'Stop'
$repo = 'maomaoHatsune/BattWhale'
$api = "https://api.github.com/repos/$repo/releases/latest"

Write-Host "querying $api"
$release = Invoke-RestMethod -Uri $api -UseBasicParsing -Headers @{ 'User-Agent' = 'BattWhale-fetch' }

$asset = $release.assets | Where-Object { $_.name -eq 'BattWhale.exe' } | Select-Object -First 1
if (-not $asset) { throw "no BattWhale.exe asset in release $($release.tag_name)" }

Write-Host ("downloading {0} ({1:N1} KB)" -f $release.tag_name, ($asset.size / 1KB))
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $Out -UseBasicParsing

# Belt and braces: drop the zone stream if anything attached one.
try { Unblock-File -Path $Out } catch { }

$zone = Get-Item -Path $Out -Stream * -ErrorAction SilentlyContinue |
        Where-Object { $_.Stream -eq 'Zone.Identifier' }
if ($zone) {
    Write-Host "WARNING: Zone.Identifier is still present on $Out"
    exit 1
}

$size = (Get-Item $Out).Length
Write-Host ("saved: {0} ({1:N1} KB) - no Mark-of-the-Web, SmartScreen will not stop it" -f $Out, ($size / 1KB))
