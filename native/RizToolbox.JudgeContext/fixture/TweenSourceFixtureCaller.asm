; Owned executable fixture only. Every table cell names a real CALL return PC.
; No source patch, copied game instruction, CLR callback or game entry is used.
OPTION CASEMAP:NONE
EXTERN RizTweenSourceContext_Entry:PROC
EXTERN RtTweenSourceFixture_Capture:PROC
EXTERN RtTweenSourceFixture_Barrier:PROC

SNAPSHOT MACRO at:req
    mov dword ptr [rsp+at+000h], 1
    mov dword ptr [rsp+at+004h], 1b0h
    mov [rsp+at+020h], rax
    mov [rsp+at+028h], rcx
    mov [rsp+at+030h], rdx
    mov [rsp+at+038h], rbx
    mov [rsp+at+040h], rsp
    mov [rsp+at+048h], rbp
    mov [rsp+at+050h], rsi
    mov [rsp+at+058h], rdi
    mov [rsp+at+060h], r8
    mov [rsp+at+068h], r9
    mov [rsp+at+070h], r10
    mov [rsp+at+078h], r11
    mov [rsp+at+080h], r12
    mov [rsp+at+088h], r13
    mov [rsp+at+090h], r14
    mov [rsp+at+098h], r15
    pushfq
    pop qword ptr [rsp+at+018h]
    movdqa [rsp+at+0a0h], xmm0
    movdqa [rsp+at+0b0h], xmm1
    movdqa [rsp+at+0c0h], xmm2
    movdqa [rsp+at+0d0h], xmm3
    movdqa [rsp+at+0e0h], xmm4
    movdqa [rsp+at+0f0h], xmm5
    movdqa [rsp+at+100h], xmm6
    movdqa [rsp+at+110h], xmm7
    movdqa [rsp+at+120h], xmm8
    movdqa [rsp+at+130h], xmm9
    movdqa [rsp+at+140h], xmm10
    movdqa [rsp+at+150h], xmm11
    movdqa [rsp+at+160h], xmm12
    movdqa [rsp+at+170h], xmm13
    movdqa [rsp+at+180h], xmm14
    movdqa [rsp+at+190h], xmm15
    stmxcsr [rsp+at+1a0h]
    fnstcw [rsp+at+1a4h]
    mov word ptr [rsp+at+1a6h], 0
    mov qword ptr [rsp+at+1a8h], 0
ENDM

SOURCE_CALL MACRO symbol:req, redirect:req
    LOCAL unchanged
    PUBLIC symbol
    PUBLIC symbol&_Return
symbol LABEL BYTE
    call RizTweenSourceContext_Entry
symbol&_Return LABEL BYTE
    ; Record the consumed real CALL PC before pushfq can reuse that stack cell.
    ; Scratch RAX is restored before the independent after-state snapshot.
    mov [rsp+488h], rax
    mov rax, [rsp-8]
    mov [rsp+490h], rax
    mov rax, [rsp+488h]
IF redirect EQ 0
    jmp RtTweenSourceFixture_CaptureState
ELSE
    ; Same selector contract as the production relay: preserve incoming flags
    ; around the comparison, then use a static JMP. Never rewrite a return PC.
    pushfq
    cmp qword ptr [rsp+8], 0
    je unchanged
    popfq
IF redirect EQ 1
    jmp RtTweenSourceFixture_DeadReadyA
ELSEIF redirect EQ 2
    jmp RtTweenSourceFixture_DeadReadyB
ELSE
    jmp RtTweenSourceFixture_DeadTail
ENDIF
unchanged:
    popfq
    jmp RtTweenSourceFixture_CaptureState
ENDIF
ENDM

.code
PUBLIC RtTweenSourceFixture_Invoke
PUBLIC RtTweenSourceFixture_DeadReadyA
PUBLIC RtTweenSourceFixture_DeadReadyB
PUBLIC RtTweenSourceFixture_DeadTail
RtTweenSourceFixture_Invoke PROC FRAME
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
    sub rsp, 4a8h
    .allocstack 4a8h
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
    ; B=RSP is aligned; B+0..1f is exclusively outgoing home space. The source
    ; entry may write selector cell zero, but it must preserve the other cells.
    mov [rsp+468h], rdx
    mov [rsp+470h], r8
    stmxcsr [rsp+460h]
    fnstcw [rsp+464h]
    mov ecx, ecx
    lea rax, RtTweenSourceFixture_Thunks
    mov rax, [rax+rcx*8]
    mov [rsp+478h], rax
    lea rax, RtTweenSourceFixture_Returns
    mov rax, [rax+rcx*8]
    mov [rsp+048h], rax
    mov [rsp+208h], rax
    lea rax, [rsp-8]
    mov [rsp+050h], rax
    mov [rsp+210h], rax
    mov rax, 0aabbccddeeff1122h
    mov [rsp+000h], rax
    mov [rsp+008h], rax
    mov [rsp+010h], rax
    mov [rsp+018h], rax
    mov [rsp+498h], rax
    mov [rsp+4a0h], rax
    mov qword ptr [rsp+480h], 0
    mov rax, [rsp+468h]
    ldmxcsr [rax+1a0h]
    fldcw [rax+1a4h]
    movdqu xmm0, [rax+0a0h]
    movdqu xmm1, [rax+0b0h]
    movdqu xmm2, [rax+0c0h]
    movdqu xmm3, [rax+0d0h]
    movdqu xmm4, [rax+0e0h]
    movdqu xmm5, [rax+0f0h]
    movdqu xmm6, [rax+100h]
    movdqu xmm7, [rax+110h]
    movdqu xmm8, [rax+120h]
    movdqu xmm9, [rax+130h]
    movdqu xmm10, [rax+140h]
    movdqu xmm11, [rax+150h]
    movdqu xmm12, [rax+160h]
    movdqu xmm13, [rax+170h]
    movdqu xmm14, [rax+180h]
    movdqu xmm15, [rax+190h]
    push qword ptr [rax+018h]
    popfq
    mov rcx, [rax+028h]
    mov rdx, [rax+030h]
    mov rbx, [rax+038h]
    mov rbp, [rax+048h]
    mov rsi, [rax+050h]
    mov rdi, [rax+058h]
    mov r8, [rax+060h]
    mov r9, [rax+068h]
    mov r10, [rax+070h]
    mov r11, [rax+078h]
    mov r12, [rax+080h]
    mov r13, [rax+088h]
    mov r14, [rax+090h]
    mov r15, [rax+098h]
    mov rax, [rax+020h]
    SNAPSHOT 040h
    jmp qword ptr [rsp+478h]

    SOURCE_CALL S0Before, 0
    SOURCE_CALL S0After, 1
    SOURCE_CALL S1Before, 0
    SOURCE_CALL S1After, 2
    SOURCE_CALL S2Before, 0
    SOURCE_CALL S2After, 0
    SOURCE_CALL S3Before, 0
    SOURCE_CALL S3After, 0
    SOURCE_CALL S4Before, 0
    SOURCE_CALL S4After, 0
    SOURCE_CALL S5Before, 0
    SOURCE_CALL S5After, 0
    SOURCE_CALL S6Before, 0
    SOURCE_CALL S6After, 0
    SOURCE_CALL S7Before, 0
    SOURCE_CALL S7After, 0
    SOURCE_CALL S8Before, 0
    SOURCE_CALL S8After, 0
    SOURCE_CALL S9Before, 0
    SOURCE_CALL S9After, 0
    SOURCE_CALL S10Before, 0
    SOURCE_CALL S10After, 0
    SOURCE_CALL S11Before, 0
    SOURCE_CALL S11After, 0
    SOURCE_CALL S12Before, 0
    SOURCE_CALL S12After, 0
    SOURCE_CALL S13Decision, 3
    SOURCE_CALL S14Before, 0
    SOURCE_CALL S14After, 0
    SOURCE_CALL S15Before, 0
    SOURCE_CALL S15After, 0
    SOURCE_CALL S16Before, 0
    SOURCE_CALL S16After, 0
    SOURCE_CALL S17Before, 0
    SOURCE_CALL S17After, 0
    SOURCE_CALL S18Before, 0
    SOURCE_CALL S18After, 0
    SOURCE_CALL S19Before, 0
    SOURCE_CALL S19After, 0
    SOURCE_CALL S20Before, 0
    SOURCE_CALL S20After, 0
    SOURCE_CALL S21Before, 0
    SOURCE_CALL S21After, 0
    SOURCE_CALL Unbound, 0

    ; The three owned code labels have the same relative distances as the
    ; fixed dead-policy destinations. Configure therefore verifies its ordinary
    ; moduleBase+RVA arithmetic with no test-only source-address bypass.
; Direct branch destinations must have a code type. LABEL BYTE is appropriate
; for recorded CALL identities, but MASM treats it as a byte memory operand in
; JMP and reports A2024 instead of encoding the intended direct branch.
RtTweenSourceFixture_DeadReadyA LABEL NEAR
    mov qword ptr [rsp+480h], 1
    jmp RtTweenSourceFixture_CaptureState
    db (074eh-($-RtTweenSourceFixture_DeadReadyA)) dup (090h)
RtTweenSourceFixture_DeadTail LABEL NEAR
    mov qword ptr [rsp+480h], 13
    jmp RtTweenSourceFixture_CaptureState
    db (07a0h-($-RtTweenSourceFixture_DeadReadyA)) dup (090h)
RtTweenSourceFixture_DeadReadyB LABEL NEAR
    mov qword ptr [rsp+480h], 2
    jmp RtTweenSourceFixture_CaptureState

RtTweenSourceFixture_CaptureState LABEL NEAR
    SNAPSHOT 200h
    ; Input DF is deliberately set. The C++ fixture capture runs only after the
    ; immutable after-state is saved and the normal Windows DF/FPU are restored.
    cld
    ldmxcsr [rsp+460h]
    fldcw [rsp+464h]
    mov rcx, [rsp+470h]
    lea rdx, [rsp+040h]
    lea r8, [rsp+200h]
    mov r9, rsp
    call RtTweenSourceFixture_Capture
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
    add rsp, 4a8h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
RtTweenSourceFixture_Invoke ENDP

; A real owned pointer store immediately followed by a native barrier-model
; CALL. The model records that it observed the stored value. No source dispatch
; or managed code is inserted between the store and that CALL.
PUBLIC RtTweenSourceFixture_StoreBarrier
RtTweenSourceFixture_StoreBarrier PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    mov [rcx], rdx
    call RtTweenSourceFixture_Barrier
    add rsp, 28h
    ret
RtTweenSourceFixture_StoreBarrier ENDP

.const
ALIGN 8
PUBLIC RtTweenSourceFixture_Thunks
PUBLIC RtTweenSourceFixture_Returns
RtTweenSourceFixture_Thunks DQ S0Before, S0After, S1Before, S1After
    DQ S2Before, S2After, S3Before, S3After, S4Before, S4After
    DQ S5Before, S5After, S6Before, S6After, S7Before, S7After
    DQ S8Before, S8After, S9Before, S9After, S10Before, S10After
    DQ S11Before, S11After, S12Before, S12After, S13Decision
    DQ S14Before, S14After, S15Before, S15After, S16Before, S16After
    DQ S17Before, S17After, S18Before, S18After, S19Before, S19After
    DQ S20Before, S20After, S21Before, S21After, Unbound
RtTweenSourceFixture_Returns DQ S0Before_Return, S0After_Return, S1Before_Return, S1After_Return
    DQ S2Before_Return, S2After_Return, S3Before_Return, S3After_Return, S4Before_Return, S4After_Return
    DQ S5Before_Return, S5After_Return, S6Before_Return, S6After_Return, S7Before_Return, S7After_Return
    DQ S8Before_Return, S8After_Return, S9Before_Return, S9After_Return, S10Before_Return, S10After_Return
    DQ S11Before_Return, S11After_Return, S12Before_Return, S12After_Return, S13Decision_Return
    DQ S14Before_Return, S14After_Return, S15Before_Return, S15After_Return, S16Before_Return, S16After_Return
    DQ S17Before_Return, S17After_Return, S18Before_Return, S18After_Return, S19Before_Return, S19After_Return
    DQ S20Before_Return, S20After_Return, S21Before_Return, S21After_Return, Unbound_Return
END
