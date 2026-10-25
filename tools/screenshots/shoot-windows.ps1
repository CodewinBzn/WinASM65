# The console screenshots of the documentation: the command line and the
# terminal monitor, each in a classic console window of a fixed size, captured
# with capture-window.ps1.
#   shoot-windows.ps1 [-Mesen C:\path\to\Mesen.exe]
param([string] $Mesen)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$images = Join-Path $repo 'docs\site\img'
$capture = Join-Path $PSScriptRoot 'capture-window.ps1'

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Windows {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    // Minimizes every visible window of a process (6 = SW_MINIMIZE).
    public static void Minimize(uint pid) {
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) ShowWindow(h, 6); return true; }, IntPtr.Zero);
    }
}
"@

Write-Output 'Building the assembler and the monitor...'
dotnet build (Join-Path $repo 'WinASM65.Monitor\WinASM65.Monitor.csproj') -c Release -v q -nologo | Out-Null
dotnet build (Join-Path $repo 'WinASM65\WinASM65.csproj') -c Release -v q -nologo | Out-Null
$assembler = Join-Path $repo 'WinASM65\bin\Release\net8.0\WinASM65.exe'
$monitor = Join-Path $repo 'WinASM65.Monitor\bin\Release\net8.0\WinASM65.Monitor.exe'

# A clean copy of the example, as committed.
$work = Join-Path ([IO.Path]::GetTempPath()) ('winasm65-shots-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $work | Out-Null
git -C $repo archive -o (Join-Path $work 'example.tar') HEAD example_star_catcher
Push-Location $work
tar -xf example.tar
Pop-Location
$example = Join-Path $work 'example_star_catcher'

# Runs a command line in a new classic console of cols x lines, waits, captures
# the window, and closes it.
function Shoot-Console([string] $name, [string[]] $commands, [int] $cols, [int] $lines, [int] $seconds, [string] $directory, [string] $hide) {
    $script = Join-Path $work ($name + '.cmd')
    Set-Content -Encoding ascii $script (@(
        '@echo off',
        'chcp 65001 >nul',
        "title WinASM65-shot-$name",
        "mode con cols=$cols lines=$lines",
        "cd /d `"$directory`"",
        "set PATH=$(Split-Path $assembler);$(Split-Path $monitor);%PATH%",
        'prompt $G$S',
        'cls',
        'echo on'
    ) + $commands + @('@pause >nul'))
    $console = Start-Process conhost.exe -ArgumentList "cmd.exe /c `"$script`"" -PassThru
    Start-Sleep -Seconds $seconds
    if ($hide) {
        # The capture is taken from the screen: the emulator's windows would cover the console.
        Get-Process $hide -ErrorAction SilentlyContinue | ForEach-Object { [Win32Windows]::Minimize([uint32] $_.Id) }
        Start-Sleep -Milliseconds 800
    }
    # The console window is found by its title: cmd keeps ours at the front
    # while the command runs.
    & $capture -TitleLike "WinASM65-shot-$name*" -FromScreen -Out (Join-Path $images ($name + '.png'))
    & taskkill /PID $console.Id /T /F | Out-Null
}

try {
    # The command line: the help, then a project build with its listing.
    Shoot-Console 'cli-help' @('WinASM65 -h') 150 30 4 $example
    Shoot-Console 'cli-build' @('WinASM65 -c config.json', 'dir /b src star_catcher.nes') 110 20 6 $example

    # A source with mistakes: the errors name the file, the line and the rule.
    Set-Content -Encoding ascii (Join-Path $work 'errors.asm') @(
        '    .org $8000',
        'start:',
        '    ldx ($10),y',
        '    lda #(1+',
        '    bne nowhere',
        '    .byte 1/0',
        '    jmp start'
    )
    Shoot-Console 'cli-errors' @('type errors.asm', 'WinASM65 -f errors.asm') 110 22 4 $work

    # The terminal monitor on the project, with Mesen2 when it is installed.
    if ($Mesen -and (Get-Process Mesen -ErrorAction SilentlyContinue)) {
        Write-Output '  Mesen2 is already open: the monitor runs without an emulator.'
        $Mesen = $null
    }
    if ($Mesen) {
        Push-Location $example
        & $assembler -c config.json | Out-Null
        Pop-Location
        Shoot-Console 'monitor-tui' @("WinASM65.Monitor --tui --emulator mesen2 --mesen `"$Mesen`" --rom star_catcher.nes --source src/game.asm 2>nul") 170 50 25 $example 'Mesen'
        Get-CimInstance Win32_Process -Filter "Name='Mesen.exe'" | Where-Object { $_.CommandLine -like "*$work*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    } else {
        # Without a bridge the monitor waits 15 s for one, then starts alone.
        Shoot-Console 'monitor-tui' @('WinASM65.Monitor --tui --source src/game.asm 2>nul') 170 50 22 $example
    }
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
