# Regenerates every screenshot of the documentation site (docs/site/img).
#   powershell -ExecutionPolicy Bypass -File tools\screenshots\shoot-all.ps1 [-Mesen C:\path\to\Mesen.exe]
# First time: npm ci; npx playwright install chromium (in tools\screenshots).
# Mesen2 is optional: without it the debug and live-monitor pictures are skipped
# or taken without an emulator. Close any open Mesen2 first: it runs as a single
# instance.
param([string] $Mesen)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path node_modules)) { npm ci; npx playwright install chromium }

    Write-Output 'VS Code'
    if ($Mesen) { node shoot-vscode.mjs --mesen $Mesen } else { node shoot-vscode.mjs }
    $vscode = $LASTEXITCODE

    Write-Output 'Graphics Designer'
    if ($Mesen) { node shoot-designer.mjs --mesen $Mesen } else { node shoot-designer.mjs }
    $designer = $LASTEXITCODE

    Write-Output 'Browser pages'
    node shoot-pages.mjs
    $pages = $LASTEXITCODE

    Write-Output 'Consoles'
    if (-not $Mesen) {
        # The emulator the VS Code script found, from the examples' config.json.
        $config = Get-Content -Raw ..\..\example_nes_game\config.json -ErrorAction SilentlyContinue
        if ($config -match '"Path"\s*:\s*"([^"]+Mesen[^"]*\.exe)"') {
            $candidate = $Matches[1] -replace '\\', '\'
            if (Test-Path $candidate) { $Mesen = $candidate }
        }
    }
    & .\shoot-windows.ps1 -Mesen $Mesen

    if ($Mesen) {
        Write-Output 'The example game'
        & .\shoot-game.ps1 -Mesen $Mesen
    } else {
        Write-Output '  Mesen2 not found: the pictures of the example game are skipped.'
    }

    if ($vscode -ne 0 -or $designer -ne 0 -or $pages -ne 0) { exit 1 }
} finally {
    Pop-Location
}
