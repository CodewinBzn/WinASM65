@echo off
rem Builds star_catcher.nes, with the listings (src\*.lst).
del src\*.o src\*.symb src\*.lst star_catcher.nes 2>nul
WinASM65 -l -c config.json
pause
