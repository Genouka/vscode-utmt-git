param(
    [string[]]$Platforms = @("win-x64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $RootDir

try {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Step 1: Building UndertaleModCli..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # & "$RootDir\build-cli.ps1" -Platforms $Platforms -Configuration $Configuration

    # if ($LASTEXITCODE -ne 0) {
    #     Write-Error "CLI build failed"
    #     exit 1
    # }

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Step 2: Compiling TypeScript extension..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    pnpm run compile

    if ($LASTEXITCODE -ne 0) {
        Write-Error "TypeScript compilation failed"
        exit 1
    }

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Step 3: Packaging VSIX..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $vsceInstalled = pnpm list @vscode/vsce 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installing @vscode/vsce..." -ForegroundColor Yellow
        pnpm add -g @vscode/vsce
    }

    npx @vscode/vsce package --no-dependencies

    if ($LASTEXITCODE -ne 0) {
        Write-Error "VSIX packaging failed"
        exit 1
    }

    $vsixFiles = Get-ChildItem $RootDir -Filter "*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($vsixFiles) {
        $sizeMB = [math]::Round($vsixFiles.Length / 1MB, 2)
        Write-Host "`n========================================" -ForegroundColor Green
        Write-Host "Package created successfully!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "  File: $($vsixFiles.FullName)" -ForegroundColor Green
        Write-Host "  Size: $sizeMB MB" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
