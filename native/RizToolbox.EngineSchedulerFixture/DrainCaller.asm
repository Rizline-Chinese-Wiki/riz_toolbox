; Self-owned complete Drain body. Only BeginSite/EndSite are patched. Each
; source E9 reaches a permanent near relay with a real CALL and chained unwind.
OPTION CASEMAP:NONE
EXTERN RtSchedulerFixture_DrainNext:PROC
EXTERN RtSchedulerFixture_DrainBody:PROC
EXTERN RtSchedulerFixture_DrainFinished:PROC
EXTERN RtSchedulerFixture_DrainPrepare:PROC
EXTERN RtSchedulerFixture_DrainCheck:PROC
EXTERN RtSchedulerFixture_XmmInput:BYTE
EXTERN RtSchedulerFixture_InputMxcsr:DWORD
EXTERN RtSchedulerFixture_InputFpcsr:WORD

SNAPSHOT MACRO snapshotOffset:req
    mov [rsp+snapshotOffset+020h], rax
    mov [rsp+snapshotOffset+028h], rcx
    mov [rsp+snapshotOffset+030h], rdx
    mov [rsp+snapshotOffset+038h], rbx
    mov [rsp+snapshotOffset+040h], rsp
    mov [rsp+snapshotOffset+048h], rbp
    mov [rsp+snapshotOffset+050h], rsi
    mov [rsp+snapshotOffset+058h], rdi
    mov [rsp+snapshotOffset+060h], r8
    mov [rsp+snapshotOffset+068h], r9
    mov [rsp+snapshotOffset+070h], r10
    mov [rsp+snapshotOffset+078h], r11
    mov [rsp+snapshotOffset+080h], r12
    mov [rsp+snapshotOffset+088h], r13
    mov [rsp+snapshotOffset+090h], r14
    mov [rsp+snapshotOffset+098h], r15
    pushfq
    pop qword ptr [rsp+snapshotOffset+018h]
    movdqa [rsp+snapshotOffset+0a0h], xmm0
    movdqa [rsp+snapshotOffset+0b0h], xmm1
    movdqa [rsp+snapshotOffset+0c0h], xmm2
    movdqa [rsp+snapshotOffset+0d0h], xmm3
    movdqa [rsp+snapshotOffset+0e0h], xmm4
    movdqa [rsp+snapshotOffset+0f0h], xmm5
    movdqa [rsp+snapshotOffset+100h], xmm6
    movdqa [rsp+snapshotOffset+110h], xmm7
    movdqa [rsp+snapshotOffset+120h], xmm8
    movdqa [rsp+snapshotOffset+130h], xmm9
    movdqa [rsp+snapshotOffset+140h], xmm10
    movdqa [rsp+snapshotOffset+150h], xmm11
    movdqa [rsp+snapshotOffset+160h], xmm12
    movdqa [rsp+snapshotOffset+170h], xmm13
    movdqa [rsp+snapshotOffset+180h], xmm14
    movdqa [rsp+snapshotOffset+190h], xmm15
    stmxcsr [rsp+snapshotOffset+1a0h]
    fnstcw [rsp+snapshotOffset+1a4h]
ENDM
STAMP MACRO
    ldmxcsr [RtSchedulerFixture_InputMxcsr]
    fldcw [RtSchedulerFixture_InputFpcsr]
    movdqa xmm0, xmmword ptr [RtSchedulerFixture_XmmInput+000h]
    movdqa xmm1, xmmword ptr [RtSchedulerFixture_XmmInput+010h]
    movdqa xmm2, xmmword ptr [RtSchedulerFixture_XmmInput+020h]
    movdqa xmm3, xmmword ptr [RtSchedulerFixture_XmmInput+030h]
    movdqa xmm4, xmmword ptr [RtSchedulerFixture_XmmInput+040h]
    movdqa xmm5, xmmword ptr [RtSchedulerFixture_XmmInput+050h]
    movdqa xmm6, xmmword ptr [RtSchedulerFixture_XmmInput+060h]
    movdqa xmm7, xmmword ptr [RtSchedulerFixture_XmmInput+070h]
    movdqa xmm8, xmmword ptr [RtSchedulerFixture_XmmInput+080h]
    movdqa xmm9, xmmword ptr [RtSchedulerFixture_XmmInput+090h]
    movdqa xmm10, xmmword ptr [RtSchedulerFixture_XmmInput+0a0h]
    movdqa xmm11, xmmword ptr [RtSchedulerFixture_XmmInput+0b0h]
    movdqa xmm12, xmmword ptr [RtSchedulerFixture_XmmInput+0c0h]
    movdqa xmm13, xmmword ptr [RtSchedulerFixture_XmmInput+0d0h]
    movdqa xmm14, xmmword ptr [RtSchedulerFixture_XmmInput+0e0h]
    movdqa xmm15, xmmword ptr [RtSchedulerFixture_XmmInput+0f0h]
    mov rax, 01111111112345678h
    mov rcx, 02222222223456789h
    mov rdx, 0333333333456789ah
    mov rbx, [rsp+7b8h]
    mov rbp, [rsp+7a8h]
    mov rsi, 0666666666789abcdh
    mov rdi, 077777777789abcdeh
    mov r8, 08888888889abcdefh
    mov r9, 0999999999abcdef0h
    mov r10, 0aaaaaaaaabcdef01h
    mov r11, 0bbbbbbbbbcdef012h
    mov r12, 0ccccccccdef01234h
    mov r13, 0ddddddddef012345h
    mov r14, 0eeeeeeee01234567h
    mov r15, 0ffffffff12345678h
ENDM
.code
PUBLIC RtSchedulerFixture_Drain
PUBLIC RtSchedulerFixture_DrainBeginSite
PUBLIC RtSchedulerFixture_DrainBeginContinuation
PUBLIC RtSchedulerFixture_DrainNoCallback
PUBLIC RtSchedulerFixture_DrainEndSite
PUBLIC RtSchedulerFixture_DrainEndContinuation
PUBLIC RtSchedulerFixture_DrainEnd
RtSchedulerFixture_Drain PROC FRAME
    push rbx
    .pushreg rbx
    push rbp
    .pushreg rbp
    push rsi
    .pushreg rsi
    push rdi
    .pushreg rdi
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    sub rsp, 7d8h
    .allocstack 7d8h
    movdqa [rsp+700h], xmm6
    .savexmm128 xmm6, 700h
    movdqa [rsp+710h], xmm7
    .savexmm128 xmm7, 710h
    movdqa [rsp+720h], xmm8
    .savexmm128 xmm8, 720h
    movdqa [rsp+730h], xmm9
    .savexmm128 xmm9, 730h
    movdqa [rsp+740h], xmm10
    .savexmm128 xmm10, 740h
    movdqa [rsp+750h], xmm11
    .savexmm128 xmm11, 750h
    movdqa [rsp+760h], xmm12
    .savexmm128 xmm12, 760h
    movdqa [rsp+770h], xmm13
    .savexmm128 xmm13, 770h
    movdqa [rsp+780h], xmm14
    .savexmm128 xmm14, 780h
    movdqa [rsp+790h], xmm15
    .savexmm128 xmm15, 790h
    .endprolog
    mov [rsp+7a8h], rcx
    stmxcsr [rsp+7a0h]
    fnstcw [rsp+7a4h]
    mov rax, 0aabbccddeeff1122h
    mov [rsp+7c0h], rax
    mov [rsp+7c8h], rax
    mov [rsp+7d0h], rax
DrainLoop:
    mov rcx, [rsp+7a8h]
    call RtSchedulerFixture_DrainNext
    test rax, rax
    jz DrainDone
    mov [rsp+7b8h], rax
    mov rcx, [rsp+7a8h]
    mov rdx, rsp
    call RtSchedulerFixture_DrainPrepare
    STAMP
    std
    stc
    SNAPSHOT 040h
RtSchedulerFixture_DrainBeginSite LABEL BYTE
    test byte ptr [rbx+54h], 40h
    jz short RtSchedulerFixture_DrainNoCallback
RtSchedulerFixture_DrainBeginContinuation LABEL BYTE
    mov byte ptr [rsp+7b0h], 1
    jmp short DrainAfterCategory
RtSchedulerFixture_DrainNoCallback LABEL NEAR
    mov byte ptr [rsp+7b0h], 0
DrainAfterCategory:
    SNAPSHOT 1f0h
    cld
    ldmxcsr [rsp+7a0h]
    fldcw [rsp+7a4h]
    mov rcx, [rsp+7a8h]
    mov rdx, [rsp+7b8h]
    movzx r8d, byte ptr [rsp+7b0h]
    call RtSchedulerFixture_DrainBody
    STAMP
    std
    stc
    SNAPSHOT 3a0h
RtSchedulerFixture_DrainEndSite LABEL BYTE
    cmp qword ptr [rbp+38h], 0
RtSchedulerFixture_DrainEndContinuation LABEL BYTE
    SNAPSHOT 550h
    cld
    ldmxcsr [rsp+7a0h]
    fldcw [rsp+7a4h]
    mov rcx, [rsp+7a8h]
    mov rdx, rsp
    call RtSchedulerFixture_DrainCheck
    jmp DrainLoop
DrainDone:
    mov rcx, [rsp+7a8h]
    call RtSchedulerFixture_DrainFinished
    ldmxcsr [rsp+7a0h]
    fldcw [rsp+7a4h]
    movdqa xmm6, [rsp+700h]
    movdqa xmm7, [rsp+710h]
    movdqa xmm8, [rsp+720h]
    movdqa xmm9, [rsp+730h]
    movdqa xmm10, [rsp+740h]
    movdqa xmm11, [rsp+750h]
    movdqa xmm12, [rsp+760h]
    movdqa xmm13, [rsp+770h]
    movdqa xmm14, [rsp+780h]
    movdqa xmm15, [rsp+790h]
    add rsp, 7d8h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
RtSchedulerFixture_DrainEnd LABEL BYTE
RtSchedulerFixture_Drain ENDP
PUBLIC RtSchedulerFixture_TestFlags
RtSchedulerFixture_TestFlags PROC
    push rdx
    popfq
    test byte ptr [rcx], 40h
    pushfq
    pop rax
    cld
    ret
RtSchedulerFixture_TestFlags ENDP
PUBLIC RtSchedulerFixture_CmpFlags
RtSchedulerFixture_CmpFlags PROC
    push rdx
    popfq
    cmp qword ptr [rcx], 0
    pushfq
    pop rax
    cld
    ret
RtSchedulerFixture_CmpFlags ENDP
END
