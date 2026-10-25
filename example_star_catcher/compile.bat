@echo off
rem Builds star_catcher.nes, with the listings (src\*.lst).
rem The graphics of gfx\ are exported first when WinASM65.Designer is on the PATH;
rem otherwise the committed exports are used.
where WinASM65.Designer >nul 2>nul && WinASM65.Designer export gfx
del src\*.o src\*.symb src\*.lst star_catcher.nes 2>nul
WinASM65 -l -c config.json
pause
