; Self-owned Start-shaped body. Only the three public labelled windows below
; may be patched by this fixture. No game image or installed payload is touched.
OPTION CASEMAP:NONE
EXTERN RtEngineInlineFixture_Prepare:PROC
EXTERN RtEngineInlineFixture_Between:PROC
EXTERN RtEngineInlineFixture_Complete:PROC
EXTERN RtEngineInlineFixture_XmmInput:BYTE
EXTERN RtEngineInlineFixture_InputMxcsr:DWORD
EXTERN RtEngineInlineFixture_InputFpcsr:WORD
EXTERN RtEngineInlineFixture_ClobberMxcsr:DWORD
EXTERN RtEngineInlineFixture_ClobberFpcsr:WORD

; Same independent full-state snapshot layout as MidContext.h (0x1b0).
SNAPSHOT MACRO offset:req
    mov [rsp+offset+020h], rax
    mov [rsp+offset+028h], rcx
    mov [rsp+offset+030h], rdx
    mov [rsp+offset+038h], rbx
    mov [rsp+offset+040h], rsp
    mov [rsp+offset+048h], rbp
    mov [rsp+offset+050h], rsi
    mov [rsp+offset+058h], rdi
    mov [rsp+offset+060h], r8
    mov [rsp+offset+068h], r9
    mov [rsp+offset+070h], r10
    mov [rsp+offset+078h], r11
    mov [rsp+offset+080h], r12
    mov [rsp+offset+088h], r13
    mov [rsp+offset+090h], r14
    mov [rsp+offset+098h], r15
    pushfq
    pop qword ptr [rsp+offset+018h]
    movdqa [rsp+offset+0a0h], xmm0
    movdqa [rsp+offset+0b0h], xmm1
    movdqa [rsp+offset+0c0h], xmm2
    movdqa [rsp+offset+0d0h], xmm3
    movdqa [rsp+offset+0e0h], xmm4
    movdqa [rsp+offset+0f0h], xmm5
    movdqa [rsp+offset+100h], xmm6
    movdqa [rsp+offset+110h], xmm7
    movdqa [rsp+offset+120h], xmm8
    movdqa [rsp+offset+130h], xmm9
    movdqa [rsp+offset+140h], xmm10
    movdqa [rsp+offset+150h], xmm11
    movdqa [rsp+offset+160h], xmm12
    movdqa [rsp+offset+170h], xmm13
    movdqa [rsp+offset+180h], xmm14
    movdqa [rsp+offset+190h], xmm15
    stmxcsr [rsp+offset+1a0h]
    fnstcw [rsp+offset+1a4h]
ENDM

STAMP MACRO
    ldmxcsr [RtEngineInlineFixture_InputMxcsr]
    fldcw [RtEngineInlineFixture_InputFpcsr]
    movdqa xmm0, xmmword ptr [RtEngineInlineFixture_XmmInput+000h]
    movdqa xmm1, xmmword ptr [RtEngineInlineFixture_XmmInput+010h]
    movdqa xmm2, xmmword ptr [RtEngineInlineFixture_XmmInput+020h]
    movdqa xmm3, xmmword ptr [RtEngineInlineFixture_XmmInput+030h]
    movdqa xmm4, xmmword ptr [RtEngineInlineFixture_XmmInput+040h]
    movdqa xmm5, xmmword ptr [RtEngineInlineFixture_XmmInput+050h]
    movdqa xmm6, xmmword ptr [RtEngineInlineFixture_XmmInput+060h]
    movdqa xmm7, xmmword ptr [RtEngineInlineFixture_XmmInput+070h]
    movdqa xmm8, xmmword ptr [RtEngineInlineFixture_XmmInput+080h]
    movdqa xmm9, xmmword ptr [RtEngineInlineFixture_XmmInput+090h]
    movdqa xmm10, xmmword ptr [RtEngineInlineFixture_XmmInput+0a0h]
    movdqa xmm11, xmmword ptr [RtEngineInlineFixture_XmmInput+0b0h]
    movdqa xmm12, xmmword ptr [RtEngineInlineFixture_XmmInput+0c0h]
    movdqa xmm13, xmmword ptr [RtEngineInlineFixture_XmmInput+0d0h]
    movdqa xmm14, xmmword ptr [RtEngineInlineFixture_XmmInput+0e0h]
    movdqa xmm15, xmmword ptr [RtEngineInlineFixture_XmmInput+0f0h]
    mov rax, 01111111112345678h
    mov rcx, 02222222223456789h
    mov rdx, 0333333333456789ah
    mov rbx, [rsp+7b8h]
    mov rbp, 05555555556789abch
    mov rsi, 0666666666789abcdh
    mov rdi, 0
    mov r8, 08888888889abcdefh
    mov r9, 0999999999abcdef0h
    mov r10, 0aaaaaaaaabcdef01h
    mov r11, 0bbbbbbbbbcdef012h
    mov r12, [rsp+7b0h]
    mov r13, 0ddddddddef012345h
    mov r14, 0eeeeeeee01234567h
    mov r15, 0ffffffff12345678h
ENDM

.code
PUBLIC RtEngineInlineFixture_Start
PUBLIC RtEngineInlineFixture_BeginSite
PUBLIC RtEngineInlineFixture_BeginContinuation
PUBLIC RtEngineInlineFixture_EndFreeSite
PUBLIC RtEngineInlineFixture_EndRetainedSite
PUBLIC RtEngineInlineFixture_CommonContinuation
PUBLIC RtEngineInlineFixture_StartEnd
RtEngineInlineFixture_Start PROC FRAME
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
    mov [rsp+7b0h], r9
    stmxcsr [rsp+7a0h]
    fnstcw [rsp+7a4h]
    mov rax, 0aabbccddeeff1122h
    mov [rsp+7c0h], rax
    mov [rsp+7c8h], rax
    mov [rsp+7d0h], rax
    mov rdx, rsp
    lea r8, [rsp+40h]
    lea r9, [rsp+1f0h]
    call RtEngineInlineFixture_Prepare
    mov [rsp+7b8h], rax
RepeatBegin:
    STAMP
    std
    stc
    SNAPSHOT 040h
RtEngineInlineFixture_BeginSite LABEL BYTE
    ; Exact fixed source window: DEC [RBX+60], MOV RCX,[RBX+68].
    db 0ffh,04bh,060h,048h,08bh,04bh,068h
RtEngineInlineFixture_BeginContinuation LABEL BYTE
    SNAPSHOT 1f0h
    cld
    mov rcx, [rsp+7a8h]
    lea rdx, [rsp+40h]
    lea r8, [rsp+1f0h]
    call RtEngineInlineFixture_Between
    test eax, 4
    jnz RepeatBegin
    test eax, eax
    jnz RetainedBranch
FreeBranch:
    STAMP
    ; The node may already be MEM_FREE. Only the separate output slot is used.
    std
    cmp byte ptr [rsp+7c0h], 0
    mov [r12], rdi
    sete al
    SNAPSHOT 3a0h
RtEngineInlineFixture_EndFreeSite LABEL BYTE
    db 0e9h
    dd RtEngineInlineFixture_CommonContinuation - ($+4)
RetainedBranch:
    STAMP
    std
    cmp byte ptr [rsp+7c0h], 0
    mov [r12], rdi
    sete al
    SNAPSHOT 3a0h
RtEngineInlineFixture_EndRetainedSite LABEL BYTE
    db 0e9h
    dd RtEngineInlineFixture_CommonContinuation - ($+4)
RtEngineInlineFixture_CommonContinuation LABEL BYTE
    SNAPSHOT 550h
    cld
    ldmxcsr [rsp+7a0h]
    fldcw [rsp+7a4h]
    mov rcx, [rsp+7a8h]
    lea rdx, [rsp+3a0h]
    lea r8, [rsp+550h]
    lea r9, [rsp+7c0h]
    call RtEngineInlineFixture_Complete
    test eax, 100h
    jz RestoreCaller
    test eax, 200h
    jnz RetainedBranch
    jmp FreeBranch
RestoreCaller:
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
RtEngineInlineFixture_StartEnd LABEL BYTE
RtEngineInlineFixture_Start ENDP

PUBLIC RtEngineInlineFixture_ClobberVolatile
RtEngineInlineFixture_ClobberVolatile PROC
    mov rax, 0deadbeef12345678h
    mov rcx, 0deadbeef23456789h
    mov rdx, 0deadbeef3456789ah
    mov r8, 0deadbeef456789abh
    mov r9, 0deadbeef56789abch
    mov r10, 0deadbeef6789abcdh
    mov r11, 0deadbeef789abcdeh
    pcmpeqd xmm0, xmm0
    pxor xmm1, xmm1
    pcmpeqd xmm2, xmm2
    pxor xmm3, xmm3
    pcmpeqd xmm4, xmm4
    pxor xmm5, xmm5
    ldmxcsr [RtEngineInlineFixture_ClobberMxcsr]
    fldcw [RtEngineInlineFixture_ClobberFpcsr]
    cld
    stc
    ret
RtEngineInlineFixture_ClobberVolatile ENDP
END
