del engine\*.o
del engine\*.symb
del engine\*.lst
del generated_Nes_Rom\*.nes
WinASM65 -l -c config.json
pause