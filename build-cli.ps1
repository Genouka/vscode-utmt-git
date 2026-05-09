Write-Host "Dont build CLI because it is not needed" -ForegroundColor Green
exit 0

param(
    [string[]]$Platforms = @("win-x64"),
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$CleanFirst
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$CliProject = Join-Path $RootDir "..\UndertaleModTool\UndertaleModCli\UndertaleModCli.csproj"
$OutputBase = Join-Path $RootDir "cli"

if (-not (Test-Path $CliProject)) {
    Write-Error "UndertaleModCli project not found at: $CliProject"
    exit 1
}

if ($CleanFirst -and (Test-Path $OutputBase)) {
    Write-Host "Cleaning existing CLI directory..." -ForegroundColor Yellow
    Remove-Item $OutputBase -Recurse -Force
}

foreach ($platform in $Platforms) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Building UndertaleModCli for $platform..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $outputDir = Join-Path $OutputBase $platform

    if (Test-Path $outputDir) {
        Remove-Item $outputDir -Recurse -Force
    }

    dotnet publish $CliProject `
        -c $Configuration `
        -r $platform `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        --output $outputDir

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build UndertaleModCli for $platform"
        exit 1
    }

    $pdbFiles = Get-ChildItem $outputDir -Filter "*.pdb" -Recurse
    foreach ($pdb in $pdbFiles) {
        Remove-Item $pdb.FullName -Force
        Write-Host "  Removed: $($pdb.Name)"
    }

    $totalSize = (Get-ChildItem $outputDir -Recurse | Measure-Object -Property Length -Sum).Sum
    $sizeMB = [math]::Round($totalSize / 1MB, 2)
    Write-Host "`n  Build complete for $platform!" -ForegroundColor Green
    Write-Host "  Output: $outputDir" -ForegroundColor Green
    Write-Host "  Size: $sizeMB MB" -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "All CLI builds complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
