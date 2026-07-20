#!/usr/bin/env pwsh
param(
    [Alias("T")]
    [string]$Target = "Deploy",

    [Alias("E")]
    [ValidateSet("c", "g")]
    [string]$Environment = "c"
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    $toolManifest = ".\.config\dotnet-tools.json"
    if (-not (Test-Path $toolManifest)) {
        dotnet new tool-manifest | Out-Null
        dotnet tool install Cake.Tool | Out-Null
    }

    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE"
    }

    $cakeArgs = @(
        "tool"
        "run"
        "dotnet-cake"
        "--"
        "--target=$Target"
        "--environment=$Environment"
    )

    & dotnet @cakeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Cake failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
