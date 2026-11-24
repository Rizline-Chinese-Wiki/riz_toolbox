OPTION CASEMAP:NONE
EXTERN RtEventMidFixture_Prepare:PROC
EXTERN RtEventMidFixture_Complete:PROC
EXTERN RtEventMidFixture_XmmInput:BYTE
EXTERN RtEventMidFixture_InputMxcsr:DWORD
EXTERN RtEventMidFixture_InputFpcsr:WORD
EXTERN RtEventMidFixture_ClobberMxcsr:DWORD
EXTERN RtEventMidFixture_ClobberFpcsr:WORD

; Fixture frame B = current RSP, aligned 16. Two complete 0x1B0 snapshots
; live at B+0x40 and B+0x200. The native helper is the code under test.
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

.code
PUBLIC RtEventMidFixture_InvokeAsm
PUBLIC RtEventMidFixture_InvokeReturn
RtEventMidFixture_InvokeAsm PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    call RtEventMidFixture_Body
RtEventMidFixture_InvokeReturn LABEL BYTE
    add rsp, 28h
    ret
RtEventMidFixture_InvokeAsm ENDP

PUBLIC RtEventMidFixture_Body
RtEventMidFixture_Body PROC FRAME
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
    sub rsp, 488h
    .allocstack 488h
    movdqa [rsp+3c0h], xmm6
    .savexmm128 xmm6, 3c0h
    movdqa [rsp+3d0h], xmm7
    .savexmm128 xmm7, 3d0h
    movdqa [rsp+3e0h], xmm8
    .savexmm128 xmm8, 3e0h
    movdqa [rsp+3f0h], xmm9
    .savexmm128 xmm9, 3f0h
    movdqa [rsp+400h], xmm10
    .savexmm128 xmm10, 400h
    movdqa [rsp+410h], xmm11
    .savexmm128 xmm11, 410h
    movdqa [rsp+420h], xmm12
    .savexmm128 xmm12, 420h
    movdqa [rsp+430h], xmm13
    .savexmm128 xmm13, 430h
    movdqa [rsp+440h], xmm14
    .savexmm128 xmm14, 440h
    movdqa [rsp+450h], xmm15
    .savexmm128 xmm15, 450h
    .endprolog
    mov [rsp+468h], rcx
    stmxcsr [rsp+460h]
    fnstcw [rsp+464h]
    mov rax, 0aabbccddeeff1122h
    mov [rsp+470h], rax
    mov [rsp+478h], rax
    mov [rsp+480h], rax
    mov rdx, rsp
    lea r8, [rsp+40h]
    lea r9, [rsp+200h]
    call RtEventMidFixture_Prepare
    ldmxcsr [RtEventMidFixture_InputMxcsr]
    fldcw [RtEventMidFixture_InputFpcsr]
    movdqa xmm0, xmmword ptr [RtEventMidFixture_XmmInput+000h]
    movdqa xmm1, xmmword ptr [RtEventMidFixture_XmmInput+010h]
    movdqa xmm2, xmmword ptr [RtEventMidFixture_XmmInput+020h]
    movdqa xmm3, xmmword ptr [RtEventMidFixture_XmmInput+030h]
    movdqa xmm4, xmmword ptr [RtEventMidFixture_XmmInput+040h]
    movdqa xmm5, xmmword ptr [RtEventMidFixture_XmmInput+050h]
    movdqa xmm6, xmmword ptr [RtEventMidFixture_XmmInput+060h]
    movdqa xmm7, xmmword ptr [RtEventMidFixture_XmmInput+070h]
    movdqa xmm8, xmmword ptr [RtEventMidFixture_XmmInput+080h]
    movdqa xmm9, xmmword ptr [RtEventMidFixture_XmmInput+090h]
    movdqa xmm10, xmmword ptr [RtEventMidFixture_XmmInput+0a0h]
    movdqa xmm11, xmmword ptr [RtEventMidFixture_XmmInput+0b0h]
    movdqa xmm12, xmmword ptr [RtEventMidFixture_XmmInput+0c0h]
    movdqa xmm13, xmmword ptr [RtEventMidFixture_XmmInput+0d0h]
    movdqa xmm14, xmmword ptr [RtEventMidFixture_XmmInput+0e0h]
    movdqa xmm15, xmmword ptr [RtEventMidFixture_XmmInput+0f0h]
    mov rcx, 02222222212345678h
    mov rdx, 03333333323456789h
    mov rbp, [rsp+088h]
    mov rsi, [rsp+090h]
    mov rdi, 0777777776789abcdh
    mov r8, 088888888789abcdeh
    mov r9, 09999999989abcdefh
    mov r10, 0aaaaaaaa9abcdef0h
    mov r11, 0bbbbbbbbabcdef01h
    mov r12, 0ccccccccbcdef012h
    mov r13, 0ddddddddcdef0123h
    mov r14, 0eeeeeeeedef01234h
    mov r15, 0ffffffffef012345h
    xor ebx, ebx
    xor eax, eax
    SNAPSHOT 040h
PUBLIC RtEventMidFixture_Site
PUBLIC RtEventMidFixture_SiteReturn
RtEventMidFixture_Site LABEL BYTE
    db 00fh, 01fh, 044h, 000h, 000h
RtEventMidFixture_SiteReturn LABEL BYTE
    SNAPSHOT 200h
    ldmxcsr [rsp+460h]
    fldcw [rsp+464h]
    mov rcx, [rsp+468h]
    lea rdx, [rsp+40h]
    lea r8, [rsp+200h]
    lea r9, [rsp+470h]
    call RtEventMidFixture_Complete
    movdqa xmm6, [rsp+3c0h]
    movdqa xmm7, [rsp+3d0h]
    movdqa xmm8, [rsp+3e0h]
    movdqa xmm9, [rsp+3f0h]
    movdqa xmm10, [rsp+400h]
    movdqa xmm11, [rsp+410h]
    movdqa xmm12, [rsp+420h]
    movdqa xmm13, [rsp+430h]
    movdqa xmm14, [rsp+440h]
    movdqa xmm15, [rsp+450h]
    add rsp, 488h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
RtEventMidFixture_Body ENDP

; Deliberately change ABI-volatile registers and CPU flags, plus safe floating
; control rounding values. The helper must restore the interrupted site state.
PUBLIC RtEventMidFixture_ClobberVolatile
RtEventMidFixture_ClobberVolatile PROC
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
    ldmxcsr [RtEventMidFixture_ClobberMxcsr]
    fldcw [RtEventMidFixture_ClobberFpcsr]
    stc
    ret
RtEventMidFixture_ClobberVolatile ENDP
END
