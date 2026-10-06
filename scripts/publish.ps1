# Builds a self-contained single-file WinDeutsch.exe into dist\ (no .NET needed on the target machine).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = 'dotnet'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
}

& $dotnet publish src/WinDeutsch.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $root 'dist\WinDeutsch.exe'
if (-not (Test-Path $exe)) { throw "Expected file not found: $exe" }
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "OK: $exe ($mb MB)"
