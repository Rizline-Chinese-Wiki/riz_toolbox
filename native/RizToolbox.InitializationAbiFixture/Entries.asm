OPTION CASEMAP:NONE
EXTERN RtInitStartBody:PROC
EXTERN RtInitGetterBody:PROC
EXTERN RtInitPlayBody:PROC
EXTERN RtInitTooltipBody:PROC
EXTERN RtInitProgressBody:PROC
EXTERN RtInitModeBody:PROC
EXTERN RtInitInstantaneousBody:PROC
EXTERN RtInitEndRangeBody:PROC
EXTERN RtJudgeActivityBody:PROC
EXTERN RtCoroutineStartBody:PROC
EXTERN RtCoroutineMoveBody:PROC
EXTERN RtCoroutineFactoryBody:PROC
EXTERN RtVoidBody0:PROC
EXTERN RtVoidBody1:PROC
EXTERN RtVoidBody2:PROC
EXTERN RtVoidBody3:PROC
EXTERN RtVoidBody4:PROC
EXTERN RtEventMidBeforeBody4:PROC
EXTERN RtEventMidActualList4:QWORD
EXTERN RtInitPlayInputRegister:DWORD

; Distinct executable entry addresses with no prologue assumptions about the C++
; compiler. No stack/register changes occur before the exact typed C++ body.
PATCHABLE_ENTRY MACRO entryName:req, bodyName:req
    ALIGN 16
    PUBLIC entryName
    entryName PROC
    REPT 32
        nop
    ENDM
        jmp bodyName
    entryName ENDP
ENDM
.code
PATCHABLE_ENTRY RtInitStartEntry, RtInitStartBody
PATCHABLE_ENTRY RtInitGetterEntry, RtInitGetterBody
PATCHABLE_ENTRY RtInitPlayEntry, RtInitPlayBody
PATCHABLE_ENTRY RtInitTooltipEntry, RtInitTooltipBody
PATCHABLE_ENTRY RtInitProgressEntry, RtInitProgressBody
PATCHABLE_ENTRY RtInitModeEntry, RtInitModeBody
PATCHABLE_ENTRY RtInitInstantaneousEntry, RtInitInstantaneousBody
PATCHABLE_ENTRY RtInitEndRangeEntry, RtInitEndRangeBody
PATCHABLE_ENTRY RtJudgeActivityEntry, RtJudgeActivityBody
PATCHABLE_ENTRY RtCoroutineStartEntry, RtCoroutineStartBody
PATCHABLE_ENTRY RtCoroutineMoveEntry, RtCoroutineMoveBody
PATCHABLE_ENTRY RtCoroutineFactoryEntry, RtCoroutineFactoryBody
PATCHABLE_ENTRY RtVoidEntry0, RtVoidBody0
PATCHABLE_ENTRY RtVoidEntry1, RtVoidBody1
PATCHABLE_ENTRY RtVoidEntry2, RtVoidBody2
PATCHABLE_ENTRY RtVoidEntry3, RtVoidBody3

; Joint fixture: a real parent entry can be patched by Dobby entirely within
; this initial NOP prefix. The independent mid CALL always returns to +0x140
; in the original image, not to the parent's relocated trampoline.
ALIGN 16
PUBLIC RtVoidEntry4
PUBLIC RtEventMidSite4
PUBLIC RtEventMidReturn4
RtVoidEntry4 PROC FRAME
    REPT 32
        nop
    ENDM
    push rbx
    .pushreg rbx
    push rbp
    .pushreg rbp
    push rsi
    .pushreg rsi
    sub rsp, 30h
    .allocstack 30h
    .endprolog
    mov [rsp+20h], rcx
    mov [rsp+28h], rdx
    ; Fixture-only independent pre-mid gate. Native callbacks in the safely
    ; installed parent have already entered, but the actual mid CALL has not.
    call RtEventMidBeforeBody4
    mov rcx, [rsp+20h]
    mov rdx, [rsp+28h]
    mov rsi, rcx
    mov rbp, qword ptr [RtEventMidActualList4]
    xor ebx, ebx
    xor eax, eax
    REPT (13bh - ($ - RtVoidEntry4))
        nop
    ENDM
RtEventMidSite4 LABEL BYTE
    db 00fh, 01fh, 044h, 000h, 000h
RtEventMidReturn4 LABEL BYTE
    mov rcx, [rsp+20h]
    mov rdx, [rsp+28h]
    call RtVoidBody4
    add rsp, 30h
    pop rsi
    pop rbp
    pop rbx
    ret
RtVoidEntry4 ENDP
; Assembly-time label assertions protect the actual caller/continuation ABI.
; REPT alone would only diagnose an overlong prefix, not a mistaken site value.
.ERRNZ (RtEventMidSite4 - RtVoidEntry4 - 13bh)
.ERRNZ (RtEventMidReturn4 - RtVoidEntry4 - 140h)

; void(entry RCX, self RDX, rawEdx R8D, method R9). The callee Boolean is
; defined only by DL. Nonzero upper bytes must never make DL=0 become true.
; Saving the actual EDX immediately before CALL makes the fixture inspectable.
PUBLIC RtInitCallPlayRaw
RtInitCallPlayRaw PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov edx, r8d
    mov r8, r9
    mov DWORD PTR [RtInitPlayInputRegister], edx
    call rax
    add rsp, 40
    ret
RtInitCallPlayRaw ENDP

; The Start caller deliberately goes through a tail-jumping relay, like the
; inspected game's SimpleDelay/SimpleWaitUntil -> MonoBehaviour wrapper.
; The helper must report the address after CALL, not an address in the relay,
; trampoline, helper or managed observer.
PUBLIC RtCoroutineCallStart
PUBLIC RtCoroutineStartCaller
RtCoroutineCallStart PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    call RtCoroutineTailStart
RtCoroutineStartCaller LABEL BYTE
    add rsp, 40
    ret
RtCoroutineCallStart ENDP
RtCoroutineTailStart PROC
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    mov r8, r9
    jmp rax
RtCoroutineTailStart ENDP

PUBLIC RtCoroutineCallMove
PUBLIC RtCoroutineMoveCaller
RtCoroutineCallMove PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    call rax
RtCoroutineMoveCaller LABEL BYTE
    add rsp, 40
    ret
RtCoroutineCallMove ENDP

PUBLIC RtCoroutineCallFactory
PUBLIC RtCoroutineFactoryCaller
RtCoroutineCallFactory PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    call rax
RtCoroutineFactoryCaller LABEL BYTE
    add rsp, 40
    ret
RtCoroutineCallFactory ENDP

; Exact independently exported return labels for void(self, MethodInfo*).
; The relay tail-jumps into the patched target without adding another return
; address: the native helper must receive callerName, including after Dobby.
VOID_CALLER MACRO callName:req, relayName:req, callerName:req
    PUBLIC callName
    PUBLIC callerName
    callName PROC FRAME
        sub rsp, 40
        .allocstack 40
        .endprolog
        call relayName
    callerName LABEL BYTE
        add rsp, 40
        ret
    callName ENDP
    relayName PROC
        mov rax, rcx
        mov rcx, rdx
        mov rdx, r8
        jmp rax
    relayName ENDP
ENDM
VOID_CALLER RtVoidCall0, RtVoidTail0, RtVoidCaller0
VOID_CALLER RtVoidCall1, RtVoidTail1, RtVoidCaller1
VOID_CALLER RtVoidCall2, RtVoidTail2, RtVoidCaller2
VOID_CALLER RtVoidCall3, RtVoidTail3, RtVoidCaller3
VOID_CALLER RtVoidCall4, RtVoidTail4, RtVoidCaller4
END
