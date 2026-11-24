option casemap:none
EXTERN StockLowerPitchBefore:PROC
EXTERN StockLowerPitchAfter:PROC
EXTERN StockEngineSetPitch:PROC
PUBLIC StockLowerSetPitch,StockLowerSetPitchEnd,StockLowerEngineReturned,StockLowerExecutableDir64Anchor
.code
ALIGN 16
StockLowerSetPitch PROC FRAME
    mov rax,rsp
    mov qword ptr [rax+10h],rbx
    mov qword ptr [rax+20h],rdi
    push rbp
    .pushreg rbp
    lea rbp,[rax-5fh]
    sub rsp,0e0h
    .allocstack 0e0h
    .savereg rbx,0f8h
    .savereg rdi,108h
    mov rbx,rcx
    movaps xmmword ptr [rax-18h],xmm6
    .savexmm128 xmm6,0d0h
    .endprolog
    movaps xmm6,xmm1
    call StockLowerPitchBefore
    test eax,eax
    jz lower_after_engine
    mov rcx,qword ptr [rbx+10h]
    movaps xmm1,xmm6
    call StockEngineSetPitch
StockLowerEngineReturned LABEL BYTE
lower_after_engine:
    mov rcx,rbx
    movaps xmm1,xmm6
    call StockLowerPitchAfter
    movaps xmm6,xmmword ptr [rsp+0d0h]
    mov rbx,qword ptr [rsp+0f8h]
    mov rdi,qword ptr [rsp+108h]
    add rsp,0e0h
    pop rbp
    ret
    DB 495-($-StockLowerSetPitch) DUP(090h)
StockLowerSetPitch ENDP
StockLowerSetPitchEnd LABEL BYTE
ALIGN 8
StockLowerExecutableDir64Anchor LABEL QWORD
    DQ OFFSET StockLowerSetPitch
END
