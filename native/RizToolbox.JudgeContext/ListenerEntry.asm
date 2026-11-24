; Ordinary CALL full-save helpers. Pre restores the original CALL stack then
; tail-jumps through callee-owned home[0]; post returns to the real stub CALL.
OPTION CASEMAP:NONE
EXTERN RizListenerContext_PreDispatch:PROC
EXTERN RizListenerContext_PostDispatch:PROC
PUBLIC RizListenerContext_PreEntry
PUBLIC RizListenerContext_PostEntry

LISTENER_ENTRY MACRO entryName, dispatchName, isPre
entryName PROC FRAME
    push r13
    .pushreg r13
    pushfq
    .allocstack 8
    sub rsp, 1e8h
    .allocstack 1e8h
    lea r13, [rsp+080h]
    .setframe r13, 080h
    mov [rsp+058h], rbx
    .savereg rbx, 058h
    mov [rsp+068h], rbp
    .savereg rbp, 068h
    mov [rsp+070h], rsi
    .savereg rsi, 070h
    mov [rsp+078h], rdi
    .savereg rdi, 078h
    mov [rsp+0a0h], r12
    .savereg r12, 0a0h
    mov [rsp+0b0h], r14
    .savereg r14, 0b0h
    mov [rsp+0b8h], r15
    .savereg r15, 0b8h
    movdqa [rsp+120h], xmm6
    .savexmm128 xmm6, 120h
    movdqa [rsp+130h], xmm7
    .savexmm128 xmm7, 130h
    movdqa [rsp+140h], xmm8
    .savexmm128 xmm8, 140h
    movdqa [rsp+150h], xmm9
    .savexmm128 xmm9, 150h
    movdqa [rsp+160h], xmm10
    .savexmm128 xmm10, 160h
    movdqa [rsp+170h], xmm11
    .savexmm128 xmm11, 170h
    movdqa [rsp+180h], xmm12
    .savexmm128 xmm12, 180h
    movdqa [rsp+190h], xmm13
    .savexmm128 xmm13, 190h
    movdqa [rsp+1a0h], xmm14
    .savexmm128 xmm14, 1a0h
    movdqa [rsp+1b0h], xmm15
    .savexmm128 xmm15, 1b0h
    .endprolog
    cld
    mov [rsp+040h], rax
    mov [rsp+048h], rcx
    mov [rsp+050h], rdx
    mov [rsp+080h], r8
    mov [rsp+088h], r9
    mov [rsp+090h], r10
    mov [rsp+098h], r11
    movdqa [rsp+0c0h], xmm0
    movdqa [rsp+0d0h], xmm1
    movdqa [rsp+0e0h], xmm2
    movdqa [rsp+0f0h], xmm3
    movdqa [rsp+100h], xmm4
    movdqa [rsp+110h], xmm5
    mov dword ptr [rsp+020h], 1
    mov dword ptr [rsp+024h], 1b0h
    lea rax, [rsp+1f8h]
    mov [rsp+030h], rax
    mov rax, [rax]
    mov [rsp+028h], rax
    mov rax, [rsp+1e8h]
    mov [rsp+038h], rax
    lea rax, [rsp+200h]
    mov [rsp+060h], rax
    mov rax, [rsp+1f0h]
    mov [rsp+0a8h], rax
    stmxcsr dword ptr [rsp+1c0h]
    fnstcw word ptr [rsp+1c4h]
    mov word ptr [rsp+1c6h], 0
    mov qword ptr [rsp+1c8h], 0
    lea rcx, [rsp+020h]
    call dispatchName
    IF isPre
        ; I=B+1f8h, home[0]=I+8. Do not replace [I], the source return PC.
        mov [rsp+200h], rax
    ENDIF
    ldmxcsr dword ptr [rsp+1c0h]
    fldcw word ptr [rsp+1c4h]
    movdqa xmm0, [rsp+0c0h]
    movdqa xmm1, [rsp+0d0h]
    movdqa xmm2, [rsp+0e0h]
    movdqa xmm3, [rsp+0f0h]
    movdqa xmm4, [rsp+100h]
    movdqa xmm5, [rsp+110h]
    movdqa xmm6, [rsp+120h]
    movdqa xmm7, [rsp+130h]
    movdqa xmm8, [rsp+140h]
    movdqa xmm9, [rsp+150h]
    movdqa xmm10, [rsp+160h]
    movdqa xmm11, [rsp+170h]
    movdqa xmm12, [rsp+180h]
    movdqa xmm13, [rsp+190h]
    movdqa xmm14, [rsp+1a0h]
    movdqa xmm15, [rsp+1b0h]
    mov rax, [rsp+040h]
    mov rcx, [rsp+048h]
    mov rdx, [rsp+050h]
    mov rbx, [rsp+058h]
    mov rbp, [rsp+068h]
    mov rsi, [rsp+070h]
    mov rdi, [rsp+078h]
    mov r8, [rsp+080h]
    mov r9, [rsp+088h]
    mov r10, [rsp+090h]
    mov r11, [rsp+098h]
    mov r12, [rsp+0a0h]
    mov r14, [rsp+0b0h]
    mov r15, [rsp+0b8h]
    push qword ptr [rsp+1e8h]
    popfq
    ; Legal FP epilog. FF25 is ModRM00 and must end this epilog.
    lea rsp, [r13+170h]
    pop r13
    IF isPre
        jmp qword ptr [RizListenerContext_PreTailPointer]
    ELSE
        ret
    ENDIF
entryName ENDP
ENDM

.code
LISTENER_ENTRY RizListenerContext_PreEntry, RizListenerContext_PreDispatch, 1
LISTENER_ENTRY RizListenerContext_PostEntry, RizListenerContext_PostDispatch, 0

; Independent leaf: no nonvolatile writes or stack adjustment to unwind.
RizListenerContext_PreTail PROC
    jmp qword ptr [rsp+8]
RizListenerContext_PreTail ENDP
.const
ALIGN 8
RizListenerContext_PreTailPointer DQ OFFSET RizListenerContext_PreTail
END
