#!/usr/bin/env pwsh
<#
.SYNOPSIS
  CC3 production deploy (short: g)
.EXAMPLE
  .\deploy\g.ps1
  .\deploy\g.ps1 -T Build
#>
param(
    [Alias("T")]
    [string]$Target = "Deploy"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "SmartInspectConsole -> CC3 Prod (g)" -ForegroundColor Cyan
    Write-Host "Target: $Target" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    & "$PSScriptRoot\_internal\build.ps1" -T $Target -E g
    if ($LASTEXITCODE -ne 0) {
        throw "_internal\build.ps1 failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "g (prod) done." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "g (prod) failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
