$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$publishDir = Join-Path $repoRoot "dist\publish"
$payloadZip = Join-Path $repoRoot "Installer\TomatoMix.Setup\payload.zip"
$installerOutDir = Join-Path $repoRoot "dist"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  1/3 Publishing TomatoMix (Self-Contained win-x64)..." -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish "$repoRoot\TomatoMix.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  2/3 Compressing application payload..." -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Start-Sleep -Milliseconds 500
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  3/3 Building Single-File Windows Installer (TomatoMix-Setup.exe)..." -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

dotnet publish "$repoRoot\Installer\TomatoMix.Setup\TomatoMix.Setup.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $installerOutDir

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

$setupExe = Join-Path $installerOutDir "TomatoMix-Setup.exe"
$sizeMb = [Math]::Round((Get-Item $setupExe).Length / 1MB, 2)

Write-Host ""
Write-Host "SUCCESS! Standalone Windows Installer created:" -ForegroundColor Green
Write-Host "  -> $setupExe ($sizeMb MB)" -ForegroundColor Yellow
