del engine\*.o
del engine\*.o_Symbol.txt
del generated_Nes_Rom\*.nes
WinASM65 -m game.json
WinASM65 -c rom_gen.json
generated_Nes_Rom\Mesen\Mesen generated_Nes_Rom\game.nes 
pause