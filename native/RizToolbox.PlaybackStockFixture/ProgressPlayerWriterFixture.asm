option casemap:none
EXTERN StockProgressPlayerTail:PROC
PUBLIC StockProgressMusicPlayer, StockProgressMusicPlayerEnd
.code
ALIGN 16
; Exact stock leaf shape; the linker supplies this owned fixture's tail rel32.
; No RF/xdata: no RSP/nonvolatile state is changed in these thirteen bytes.
StockProgressMusicPlayer PROC
    mov qword ptr [rcx+38h],rdx
    add rcx,38h
    jmp StockProgressPlayerTail
StockProgressMusicPlayer ENDP
StockProgressMusicPlayerEnd LABEL BYTE
END
