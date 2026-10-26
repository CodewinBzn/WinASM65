# The pictures of the example game (docs/site/img/game-*.png): Star Catcher is
# built from a clean copy, played by game.lua in Mesen2's test runner, and each
# 256 x 240 frame is enlarged three times without smoothing.
#   shoot-game.ps1 -Mesen C:\path\to\Mesen.exe
param([Parameter(Mandatory = $true)] [string] $Mesen)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$images = Join-Path $repo 'docs\site\img'
Add-Type -AssemblyName System.Drawing

dotnet build (Join-Path $repo 'WinASM65\WinASM65.csproj') -c Release -v q -nologo | Out-Null
$assembler = Join-Path $repo 'WinASM65\bin\Release\net8.0\WinASM65.exe'

$work = Join-Path ([IO.Path]::GetTempPath()) ('winasm65-game-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $work | Out-Null
try {
    git -C $repo archive -o (Join-Path $work 'example.tar') HEAD example_star_catcher
    Push-Location $work
    tar -xf example.tar
    Pop-Location
    $example = Join-Path $work 'example_star_catcher'
    Push-Location $example
    & $assembler -c config.json | Out-Null
    Pop-Location

    $frames = Join-Path $work 'frames'
    New-Item -ItemType Directory $frames | Out-Null
    $script = Join-Path $work 'game.lua'
    # The variables' addresses, from the build's symbols (names are case-insensitive).
    $symbols = @{}
    (Get-Content -Raw (Join-Path $example 'src\game.symb') | ConvertFrom-Json).PSObject.Properties |
        ForEach-Object { $symbols[$_.Name.ToLowerInvariant()] = $_.Value }
    $lua = (Get-Content -Raw (Join-Path $PSScriptRoot 'game.lua')).Replace('__OUT__', $frames.Replace('\', '/'))
    $lua = [regex]::Replace($lua, '__SYM_(\w+)__', { param($m) [string]$symbols[$m.Groups[1].Value.ToLowerInvariant()] })
    $lua | Set-Content -Encoding ascii $script

    $run = Start-Process $Mesen -ArgumentList "--testRunner `"$script`" `"$(Join-Path $example 'star_catcher.nes')`"" -PassThru -WindowStyle Hidden
    if (-not $run.WaitForExit(300000)) { $run.Kill(); throw 'Mesen2 did not finish the game in 5 minutes.' }

    foreach ($name in 'game-title', 'game-play', 'game-boss', 'game-3d', 'game-continue', 'game-over') {
        $source = [System.Drawing.Image]::FromFile((Join-Path $frames "$name.png"))
        $big = New-Object System.Drawing.Bitmap ($source.Width * 3), ($source.Height * 3)
        $graphics = [System.Drawing.Graphics]::FromImage($big)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $graphics.DrawImage($source, 0, 0, $big.Width, $big.Height)
        $graphics.Dispose()
        $source.Dispose()
        $out = Join-Path $images "$name.png"
        $big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $big.Dispose()
        Write-Output "  $out"
    }

    # The tiles, drawn by the designer with the ship's palette (sprite palette 0).
    dotnet build (Join-Path $repo 'WinASM65.Designer\WinASM65.Designer.csproj') -c Release -v q -nologo | Out-Null
    $designer = Join-Path $repo 'WinASM65.Designer\bin\Release\net8.0\WinASM65.Designer.exe'
    $tiles = Join-Path $images 'game-tiles.png'
    $sheet = Join-Path $work 'tiles.png'
    & $designer render (Join-Path $example 'chr\tiles.chr') --item '../gfx/game.pal.json#4' --scale 4 -o $sheet | Out-Null
    # Pattern table 0 only, the square top half: the game's tiles are all there.
    $source = [System.Drawing.Bitmap]::FromFile($sheet)
    $table = $source.Clone((New-Object System.Drawing.Rectangle 0, 0, $source.Width, $source.Width), $source.PixelFormat)
    $source.Dispose()
    $table.Save($tiles, [System.Drawing.Imaging.ImageFormat]::Png)
    $table.Dispose()
    Write-Output "  $tiles"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
