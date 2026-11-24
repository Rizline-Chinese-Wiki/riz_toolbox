option casemap:none
EXTERN StockProgressClipTail:PROC
PUBLIC StockProgressClip, StockProgressClipEnd
.code
ALIGN 16
; A separately issued leaf profile. There is exactly one reference store.
; The owned tail verifies it and never writes the field a second time.
; No RF/xdata: these thirteen bytes change no RSP or nonvolatile register.
StockProgressClip PROC
    mov qword ptr [rcx+30h],rdx
    add rcx,30h
    jmp StockProgressClipTail
StockProgressClip ENDP
StockProgressClipEnd LABEL BYTE
END
