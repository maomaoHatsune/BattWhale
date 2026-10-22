param(
    [switch]$Clean,
    [string]$Out = 'BattWhale.exe'
)

# ASCII-only on purpose: PowerShell 5.1 reads BOM-less scripts as ANSI, and any
# Chinese literal here would turn into mojibake file names.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if ($Clean -and (Test-Path $bin)) { Remove-Item -Recurse -Force $bin }
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# --- app icon (64x64 battery) ---
$icoPath = Join-Path $root 'app.ico'
if (-not (Test-Path $icoPath)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 64, 64
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 245, 245))
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(76, 209, 132))
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(245, 245, 245)), 3
    $g.FillRectangle($white, 50, 24, 6, 16)
    $g.FillRectangle($green, 12, 22, 34, 20)
    $g.DrawRectangle($outline, 8, 18, 42, 28)
    $g.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
    $icon.Save($fs)
    $fs.Close()
    $bmp.Dispose()
}

$sources = Get-ChildItem -Path $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
$out = Join-Path $bin $Out
if (Test-Path $out) { Remove-Item $out -Force }

# --- WinRT metadata for Windows.Gaming.Input (controller battery) ---
$md = Join-Path $env:WINDIR 'System32\WinMetadata'
$winmd = @()
foreach ($n in @('Windows.Foundation', 'Windows.Gaming', 'Windows.Devices', 'Windows.System')) {
    $f = Join-Path $md ($n + '.winmd')
    if (-not (Test-Path $f)) { throw "WinRT metadata not found: $f (Windows 10/11 is required)" }
    $winmd += "/reference:$f"
}

$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
    '/utf8output', '/warn:4',
    "/out:$out",
    "/win32icon:$icoPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.dll',
    '/reference:System.ObjectModel.dll',
    '/reference:System.Runtime.WindowsRuntime.dll'
) + $winmd + $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }

$exe = Get-Item $out
Write-Host ("Built: {0} ({1:N1} KB)" -f $exe.FullName, ($exe.Length / 1KB))

