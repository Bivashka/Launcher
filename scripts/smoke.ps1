param(
    [string]$ApiBaseUrl = "",
    [switch]$SkipApi
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    Write-Host "[1/3] dotnet test backend/BivLauncher.sln"
    dotnet test backend/BivLauncher.sln

    Write-Host "[2/3] dotnet build launcher/BivLauncher.Launcher.sln"
    dotnet build launcher/BivLauncher.Launcher.sln

    Write-Host "[3/3] npm run build (admin)"
    Push-Location (Join-Path $repoRoot "admin")
    try {
        npm run build
    }
    finally {
        Pop-Location
    }

    if (-not $SkipApi -and -not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
        $healthUrl = "$($ApiBaseUrl.TrimEnd('/'))/health"
        Write-Host "[api] GET $healthUrl"
        $response = Invoke-WebRequest -Uri $healthUrl -Method Get -UseBasicParsing -TimeoutSec 15
        if ($response.StatusCode -ne 200) {
            throw "Health check failed with HTTP $($response.StatusCode)."
        }
    }

    Write-Host "Smoke checks passed."
}
finally {
    Pop-Location
}
