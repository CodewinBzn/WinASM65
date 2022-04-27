del *.o
del *.lst
del *.nes
del *.symb

WinASM65 -l -m source.json -c nes.json
@pause