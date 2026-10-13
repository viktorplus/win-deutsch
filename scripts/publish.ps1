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

# Ready-to-unpack archive: the exe and the dictionaries folder next to it (no .pdb).
$zip = Join-Path $root 'dist\WinDeutsch-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $exe, (Join-Path $root 'dist\dictionaries') -DestinationPath $zip
$zmb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "OK: $zip ($zmb MB)"
