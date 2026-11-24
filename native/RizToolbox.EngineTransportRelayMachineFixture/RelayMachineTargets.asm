OPTION CASEMAP:NONE
EXTERN RtRelayMachineInput:QWORD
EXTERN RtRelayMachineTarget:QWORD
EXTERN RtRelayMachineSourceRsp:QWORD
EXTERN RtRelayMachineCallerRsp:QWORD
EXTERN RtRelayMachineCallerXmm:BYTE
EXTERN RtRelayMachineAfterNv:QWORD

; Owned ABI harness. All source frames below are built by actual PUSH/SUB/
; MOV save instructions, never by a fabricated CONTEXT or synthetic return slot.
; The prepared relay is entered by JMP after its source RSP is 16-byte aligned.
; MidContext gpr[16] at input+20h, xmm[16] at A0h, MXCSR/FPCW at 1A0h/1A4h.

LOAD_LIVE MACRO
    mov qword ptr [RtRelayMachineSourceRsp], rsp
    mov r10, qword ptr [RtRelayMachineInput]
    ldmxcsr dword ptr [r10+1a0h]
    fldcw word ptr [r10+1a4h]
    movdqa xmm0, xmmword ptr [r10+0a0h]
    movdqa xmm1, xmmword ptr [r10+0b0h]
    movdqa xmm2, xmmword ptr [r10+0c0h]
    movdqa xmm3, xmmword ptr [r10+0d0h]
    movdqa xmm4, xmmword ptr [r10+0e0h]
    movdqa xmm5, xmmword ptr [r10+0f0h]
    movdqa xmm6, xmmword ptr [r10+100h]
    movdqa xmm7, xmmword ptr [r10+110h]
    movdqa xmm8, xmmword ptr [r10+120h]
    movdqa xmm9, xmmword ptr [r10+130h]
    movdqa xmm10, xmmword ptr [r10+140h]
    movdqa xmm11, xmmword ptr [r10+150h]
    movdqa xmm12, xmmword ptr [r10+160h]
    movdqa xmm13, xmmword ptr [r10+170h]
    movdqa xmm14, xmmword ptr [r10+180h]
    movdqa xmm15, xmmword ptr [r10+190h]
    mov rax, qword ptr [r10+020h]
    mov rcx, qword ptr [r10+028h]
    mov rdx, qword ptr [r10+030h]
    mov rbx, qword ptr [r10+038h]
    mov rbp, qword ptr [r10+048h]
    mov rsi, qword ptr [r10+050h]
    mov rdi, qword ptr [r10+058h]
    mov r8, qword ptr [r10+060h]
    mov r9, qword ptr [r10+068h]
    mov r11, qword ptr [r10+078h]
    mov r12, qword ptr [r10+080h]
    mov r13, qword ptr [r10+088h]
    mov r14, qword ptr [r10+090h]
    mov r15, qword ptr [r10+098h]
    ; TF is armed by a real POPFQ only after all source saves are complete.
    push qword ptr [r10+018h]
    popfq
    mov r10, qword ptr [r10+070h]
    jmp qword ptr [RtRelayMachineTarget]
ENDM

.code
PUBLIC RtRelayMachineTargetsBegin,RtRelayMachineTargetsEnd
RtRelayMachineTargetsBegin LABEL BYTE
ALIGN 16
PUBLIC RtRelayMachineInvoke,RtRelayMachineInvokeReturn
RtRelayMachineInvoke PROC FRAME
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
    sub rsp, 0f8h
    .allocstack 0f8h
    movdqa xmmword ptr [rsp+020h], xmm6
    .savexmm128 xmm6, 020h
    movdqa xmmword ptr [rsp+030h], xmm7
    .savexmm128 xmm7, 030h
    movdqa xmmword ptr [rsp+040h], xmm8
    .savexmm128 xmm8, 040h
    movdqa xmmword ptr [rsp+050h], xmm9
    .savexmm128 xmm9, 050h
    movdqa xmmword ptr [rsp+060h], xmm10
    .savexmm128 xmm10, 060h
    movdqa xmmword ptr [rsp+070h], xmm11
    .savexmm128 xmm11, 070h
    movdqa xmmword ptr [rsp+080h], xmm12
    .savexmm128 xmm12, 080h
    movdqa xmmword ptr [rsp+090h], xmm13
    .savexmm128 xmm13, 090h
    movdqa xmmword ptr [rsp+0a0h], xmm14
    .savexmm128 xmm14, 0a0h
    movdqa xmmword ptr [rsp+0b0h], xmm15
    .savexmm128 xmm15, 0b0h
    .endprolog
    mov qword ptr [rsp+0c8h], rcx
    mov qword ptr [RtRelayMachineCallerRsp], rsp
    stmxcsr dword ptr [rsp+0c0h]
    fnstcw word ptr [rsp+0c4h]
    mov rbx, 01122334455667788h
    mov rbp, 02233445566778899h
    mov rsi, 033445566778899aah
    mov rdi, 0445566778899aabbh
    mov r12, 05566778899aabbcch
    mov r13, 066778899aabbccddh
    mov r14, 0778899aabbccddeeh
    mov r15, 08899aabbccddeeffh
    movdqa xmm0, xmmword ptr [RtRelayMachineCallerXmm+000h]
    movdqa xmm1, xmmword ptr [RtRelayMachineCallerXmm+010h]
    movdqa xmm2, xmmword ptr [RtRelayMachineCallerXmm+020h]
    movdqa xmm3, xmmword ptr [RtRelayMachineCallerXmm+030h]
    movdqa xmm4, xmmword ptr [RtRelayMachineCallerXmm+040h]
    movdqa xmm5, xmmword ptr [RtRelayMachineCallerXmm+050h]
    movdqa xmm6, xmmword ptr [RtRelayMachineCallerXmm+060h]
    movdqa xmm7, xmmword ptr [RtRelayMachineCallerXmm+070h]
    movdqa xmm8, xmmword ptr [RtRelayMachineCallerXmm+080h]
    movdqa xmm9, xmmword ptr [RtRelayMachineCallerXmm+090h]
    movdqa xmm10, xmmword ptr [RtRelayMachineCallerXmm+0a0h]
    movdqa xmm11, xmmword ptr [RtRelayMachineCallerXmm+0b0h]
    movdqa xmm12, xmmword ptr [RtRelayMachineCallerXmm+0c0h]
    movdqa xmm13, xmmword ptr [RtRelayMachineCallerXmm+0d0h]
    movdqa xmm14, xmmword ptr [RtRelayMachineCallerXmm+0e0h]
    movdqa xmm15, xmmword ptr [RtRelayMachineCallerXmm+0f0h]
    call qword ptr [rsp+0c8h]
RtRelayMachineInvokeReturn LABEL BYTE
    cld
    mov qword ptr [RtRelayMachineAfterNv+0], rbx
    mov qword ptr [RtRelayMachineAfterNv+8], rbp
    mov qword ptr [RtRelayMachineAfterNv+16], rsi
    mov qword ptr [RtRelayMachineAfterNv+24], rdi
    mov qword ptr [RtRelayMachineAfterNv+32], r12
    mov qword ptr [RtRelayMachineAfterNv+40], r13
    mov qword ptr [RtRelayMachineAfterNv+48], r14
    mov qword ptr [RtRelayMachineAfterNv+56], r15
    ldmxcsr dword ptr [rsp+0c0h]
    fldcw word ptr [rsp+0c4h]
    movdqa xmm6, xmmword ptr [rsp+020h]
    movdqa xmm7, xmmword ptr [rsp+030h]
    movdqa xmm8, xmmword ptr [rsp+040h]
    movdqa xmm9, xmmword ptr [rsp+050h]
    movdqa xmm10, xmmword ptr [rsp+060h]
    movdqa xmm11, xmmword ptr [rsp+070h]
    movdqa xmm12, xmmword ptr [rsp+080h]
    movdqa xmm13, xmmword ptr [rsp+090h]
    movdqa xmm14, xmmword ptr [rsp+0a0h]
    movdqa xmm15, xmmword ptr [rsp+0b0h]
    add rsp, 0f8h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
RtRelayMachineInvoke ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeStart,RtRelayMachineCleanupStart
RtRelayMachineShapeStart PROC FRAME
    mov qword ptr [rsp+008h], rbx
    mov qword ptr [rsp+018h], rsi
    mov qword ptr [rsp+020h], rdi
    push rbp
    .pushreg rbp
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    sub rsp, 140h
    .allocstack 140h
    .savereg rbx, 170h
    .savereg rsi, 180h
    .savereg rdi, 188h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeStart ENDP
RtRelayMachineCleanupStart PROC
    mov rbx, qword ptr [rsp+170h]
    mov rsi, qword ptr [rsp+180h]
    mov rdi, qword ptr [rsp+188h]
    add rsp, 140h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbp
    ret
RtRelayMachineCleanupStart ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeDrain,RtRelayMachineCleanupDrain
RtRelayMachineShapeDrain PROC FRAME
    push rbp
    .pushreg rbp
    sub rsp, 30h
    .allocstack 30h
    mov qword ptr [rsp+048h], rbx
    .savereg rbx, 048h
    mov qword ptr [rsp+050h], rsi
    .savereg rsi, 050h
    mov qword ptr [rsp+028h], rdi
    .savereg rdi, 028h
    mov qword ptr [rsp+020h], r14
    .savereg r14, 020h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeDrain ENDP
RtRelayMachineCleanupDrain PROC
    mov rbx, qword ptr [rsp+048h]
    mov rsi, qword ptr [rsp+050h]
    mov rdi, qword ptr [rsp+028h]
    mov r14, qword ptr [rsp+020h]
    add rsp, 30h
    pop rbp
    ret
RtRelayMachineCleanupDrain ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeYield,RtRelayMachineCleanupYield
RtRelayMachineShapeYield PROC FRAME
    mov qword ptr [rsp+008h], rbx
    mov qword ptr [rsp+018h], rsi
    mov qword ptr [rsp+020h], rdi
    push rbp
    .pushreg rbp
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    sub rsp, 0a0h
    .allocstack 0a0h
    .savereg rbx, 0c0h
    .savereg rsi, 0d0h
    .savereg rdi, 0d8h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeYield ENDP
RtRelayMachineCleanupYield PROC
    mov rbx, qword ptr [rsp+0c0h]
    mov rsi, qword ptr [rsp+0d0h]
    mov rdi, qword ptr [rsp+0d8h]
    add rsp, 0a0h
    pop r15
    pop r14
    pop rbp
    ret
RtRelayMachineCleanupYield ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeDetach,RtRelayMachineCleanupDetach
RtRelayMachineShapeDetach PROC FRAME
    mov qword ptr [rsp+020h], rbp
    push rdi
    .pushreg rdi
    sub rsp, 20h
    .allocstack 20h
    .savereg rbp, 048h
    mov qword ptr [rsp+030h], rbx
    .savereg rbx, 030h
    mov qword ptr [rsp+038h], rsi
    .savereg rsi, 038h
    mov qword ptr [rsp+040h], r14
    .savereg r14, 040h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeDetach ENDP
RtRelayMachineCleanupDetach PROC
    mov rbx, qword ptr [rsp+030h]
    mov rsi, qword ptr [rsp+038h]
    mov r14, qword ptr [rsp+040h]
    mov rbp, qword ptr [rsp+048h]
    add rsp, 20h
    pop rdi
    ret
RtRelayMachineCleanupDetach ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeRelease,RtRelayMachineCleanupRelease
RtRelayMachineShapeRelease PROC FRAME
    push rbx
    .pushreg rbx
    sub rsp, 20h
    .allocstack 20h
    mov qword ptr [rsp+030h], rdi
    .savereg rdi, 030h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeRelease ENDP
RtRelayMachineCleanupRelease PROC
    mov rdi, qword ptr [rsp+030h]
    add rsp, 20h
    pop rbx
    ret
RtRelayMachineCleanupRelease ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeDispatch,RtRelayMachineCleanupDispatch
RtRelayMachineShapeDispatch PROC FRAME
    push rdi
    .pushreg rdi
    sub rsp, 150h
    .allocstack 150h
    .endprolog
    ; Late source RBX home save has NOT happened at any of its four sites.
    mov rax, 0bad0bad1bad2bad3h
    mov qword ptr [rsp+168h], rax
    LOAD_LIVE
RtRelayMachineShapeDispatch ENDP
RtRelayMachineCleanupDispatch PROC
    add rsp, 150h
    pop rdi
    ret
RtRelayMachineCleanupDispatch ENDP

ALIGN 16
PUBLIC RtRelayMachineShapeBatch,RtRelayMachineCleanupBatch
RtRelayMachineShapeBatch PROC FRAME
    mov qword ptr [rsp+018h], rbx
    push rsi
    .pushreg rsi
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    sub rsp, 40h
    .allocstack 40h
    .savereg rbx, 070h
    mov qword ptr [rsp+068h], rdi
    .savereg rdi, 068h
    mov qword ptr [rsp+060h], rbp
    .savereg rbp, 060h
    .endprolog
    LOAD_LIVE
RtRelayMachineShapeBatch ENDP
RtRelayMachineCleanupBatch PROC
    mov rbx, qword ptr [rsp+070h]
    mov rdi, qword ptr [rsp+068h]
    mov rbp, qword ptr [rsp+060h]
    add rsp, 40h
    pop r15
    pop r14
    pop rsi
    ret
RtRelayMachineCleanupBatch ENDP

ALIGN 16
PUBLIC RtRelayMachineDisturb
RtRelayMachineDisturb PROC
    mov rax, 0f123456789abcdefh
    mov rcx, rax
    mov rdx, rax
    mov r8, rax
    mov r9, rax
    mov r10, rax
    mov r11, rax
    pcmpeqd xmm0, xmm0
    pcmpeqd xmm1, xmm1
    pcmpeqd xmm2, xmm2
    pcmpeqd xmm3, xmm3
    pcmpeqd xmm4, xmm4
    pcmpeqd xmm5, xmm5
    sub rsp, 8
    mov dword ptr [rsp], 1f80h
    ldmxcsr dword ptr [rsp]
    mov word ptr [rsp], 037fh
    fldcw word ptr [rsp]
    add rsp, 8
    cmp rax, rcx
    ret
RtRelayMachineDisturb ENDP
RtRelayMachineTargetsEnd LABEL BYTE
END
