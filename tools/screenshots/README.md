# Documentation screenshots

Takes every picture of the documentation site (`docs/site/img`) from the real
programs, so they can be retaken whenever the UI changes.

```powershell
cd tools\screenshots
npm ci
npx playwright install chromium
powershell -ExecutionPolicy Bypass -File shoot-all.ps1 [-Mesen C:\path\to\Mesen.exe]
```

| Script | Takes |
|---|---|
| `shoot-vscode.mjs` | VS Code (the build `@vscode/test-electron` caches) with the extension under development, driven by Playwright on a clean copy of the example game (`example_star_catcher`): editor, hovers, context menu, listing column, diagnostics, build task, and with Mesen2 a debug session, the RAM panel and the emulator window |
| `shoot-pages.mjs` | The module viewer's report on `samples/lib.asm` + `samples/app.asm`, in Chromium |
| `shoot-game.ps1` + `game.lua` | The example game itself: Star Catcher played by a small autopilot in Mesen2's test runner (title, play, boss, 3D stage, game over, enlarged 3x), and its tile sheet |
| `shoot-windows.ps1` | The command line and the terminal monitor, each in a classic console window |
| `capture-window.ps1` | Captures one window to a PNG (used by the two above) |
| `check-site.mjs` | Opens every page of the site and fails on a broken link, a missing image or a script error |

Mesen2 is optional. Close it before a run: it is a single-instance program, and
a new launch would be handed to the open window.
