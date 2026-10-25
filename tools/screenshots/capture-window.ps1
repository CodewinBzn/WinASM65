# Captures the main window of a process to a PNG, even when it is behind others
# (PrintWindow asks the window to paint itself into our bitmap).
#   capture-window.ps1 -ProcessId 1234 -Out img.png
#   capture-window.ps1 -ProcessName Mesen -Out img.png
param(
    [int] $ProcessId,
    [string] $ProcessName,
    [string] $TitleLike,
    [Parameter(Mandatory = $true)] [string] $Out,
    [int] $TimeoutSeconds = 20,
    [switch] $FromScreen
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT rect, int size);
}
"@

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$handle = [IntPtr]::Zero
while ($handle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    $candidates = if ($ProcessId) { Get-Process -Id $ProcessId -ErrorAction SilentlyContinue }
                  elseif ($ProcessName) { Get-Process -Name $ProcessName -ErrorAction SilentlyContinue }
                  else { Get-Process }
    foreach ($p in $candidates) {
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and (-not $TitleLike -or $p.MainWindowTitle -like $TitleLike)) {
            $handle = $p.MainWindowHandle
            break
        }
    }
    if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
}
if ($handle -eq [IntPtr]::Zero) {
    Write-Error "No window found."
    exit 1
}

# The visible bounds, without the invisible resize border of Windows 10/11.
$rect = New-Object Win32Capture+RECT
if ([Win32Capture]::DwmGetWindowAttribute($handle, 9, [ref] $rect, 16) -ne 0) {
    [void] [Win32Capture]::GetWindowRect($handle, [ref] $rect)
}
$outer = New-Object Win32Capture+RECT
[void] [Win32Capture]::GetWindowRect($handle, [ref] $outer)

$width = $outer.Right - $outer.Left
$height = $outer.Bottom - $outer.Top
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
if ($FromScreen) {
    # What is on the screen, for windows that do not paint into PrintWindow
    # (a console showing its alternate screen buffer).
    [void] [Win32Capture]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 700
    $graphics.CopyFromScreen($outer.Left, $outer.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
} else {
    $hdc = $graphics.GetHdc()
    # 2 = PW_RENDERFULLCONTENT: also windows drawn by DirectX.
    [void] [Win32Capture]::PrintWindow($handle, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
}
$graphics.Dispose()

$crop = New-Object System.Drawing.Rectangle ($rect.Left - $outer.Left), ($rect.Top - $outer.Top), ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
$visible = $bitmap.Clone($crop, $bitmap.PixelFormat)
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$visible.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$visible.Dispose()
$bitmap.Dispose()
Write-Output "  $Out"
