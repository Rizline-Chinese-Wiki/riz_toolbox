option casemap:none
EXTERN StockLowerTimeBefore:PROC
EXTERN StockLowerTimeAfter:PROC
PUBLIC StockLowerSetTime,StockLowerSetTimeEnd,StockLowerTimeExecutableDir64Anchor
.code
ALIGN 16
StockLowerSetTime PROC FRAME
    mov qword ptr [rsp+10h],rbx
    .savereg rbx,88h
    push rbp
    .pushreg rbp
    sub rsp,70h
    .allocstack 70h
    mov rbx,rcx
    movaps xmmword ptr [rsp+60h],xmm6
    .savexmm128 xmm6,60h
    .endprolog
    movaps xmm6,xmm1
    mov r8,qword ptr [rsp+78h]
    call StockLowerTimeBefore
    movd dword ptr [rbx+84h],xmm6
    mov rcx,rbx
    movaps xmm1,xmm6
    call StockLowerTimeAfter
    movaps xmm6,xmmword ptr [rsp+60h]
    mov rbx,qword ptr [rsp+88h]
    add rsp,70h
    pop rbp
    ret
    DB 305-($-StockLowerSetTime) DUP(090h)
StockLowerSetTime ENDP
StockLowerSetTimeEnd LABEL BYTE
ALIGN 8
StockLowerTimeExecutableDir64Anchor LABEL QWORD
    DQ OFFSET StockLowerSetTime
END
