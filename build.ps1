param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

$src = Join-Path $PSScriptRoot "src"
$dist = Join-Path $PSScriptRoot "dist"
$icon = Join-Path $PSScriptRoot "assets\app.ico"
$out = Join-Path $dist "ForceDeleter.exe"

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$sources = @(Get-ChildItem -LiteralPath $src -Filter "*.cs" | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No source files found in $src" }

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/out:$out",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll"
)

if (Test-Path -LiteralPath $icon) {
    $arguments += "/win32icon:$icon"
}
if ($Configuration -eq "Debug") {
    $arguments += "/debug+"
    $arguments += "/define:DEBUG"
}

$arguments += $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $out"
