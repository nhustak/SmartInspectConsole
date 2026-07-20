#!/usr/bin/env pwsh
<#
.SYNOPSIS
  SureCourt deploy (short: c)
.EXAMPLE
  .\deploy\c.ps1
  .\deploy\c.ps1 -T Build
#>
param(
    [Alias("T")]
    [string]$Target = "Deploy"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "SmartInspectConsole -> SureCourt (c)" -ForegroundColor Cyan
    Write-Host "Target: $Target" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    & "$PSScriptRoot\_internal\build.ps1" -T $Target -E c
    if ($LASTEXITCODE -ne 0) {
        throw "_internal\build.ps1 failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "c (SureCourt) done." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "c (SureCourt) failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
