option casemap:none
EXTERN StockStopEntry:QWORD
EXTERN StockStopMethod:QWORD
PUBLIC StockStopTarget, StockStopReplayCaller, StockStopReplayReturned
PUBLIC StockStopResumeCaller, StockStopResumeReturned
.code
; Tail forwarding preserves the actual E8 return PC. No patched game code or
; caller-supplied frame is involved in this isolated protocol fixture.
StockStopTarget PROC
    jmp QWORD PTR [StockStopEntry]
StockStopTarget ENDP
StockStopReplayCaller PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rdx,QWORD PTR [StockStopMethod]
    call StockStopTarget
StockStopReplayReturned LABEL BYTE
    add rsp,28h
    ret
StockStopReplayCaller ENDP
StockStopResumeCaller PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rdx,QWORD PTR [StockStopMethod]
    call StockStopTarget
StockStopResumeReturned LABEL BYTE
    add rsp,28h
    ret
StockStopResumeCaller ENDP
END
