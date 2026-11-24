OPTION CASEMAP:NONE
EXTERN RtTransportActiveCall:QWORD
EXTERN RtTransportTraceRequested:DWORD
EXTERN RtTransportOwnedBody:PROC

; Exact fixed prefix bytes, with owned representative continuation bodies.
; Each original jumps to its matching baseline PrefixEnd. No source is patched.
CAPTURE_TAIL MACRO
    mov r10, qword ptr [RtTransportActiveCall]
    mov qword ptr [r10+64], rax
    mov qword ptr [r10+72], r11
    mov qword ptr [r10+80], rsp
    pushfq
    pop qword ptr [r10+88]
    mov qword ptr [r10+96], rcx
    mov qword ptr [r10+104], rdx
    mov qword ptr [r10+112], r8
    mov qword ptr [r10+120], r9
    inc dword ptr [r10+248]
ENDM
CAPTURE_ZF MACRO
    LOCAL ZfFalse, ZfDone
    mov r10, qword ptr [RtTransportActiveCall]
    ; Execute both real Jcc paths across the independently selected inputs.
    ; MOV/JMP preserve the original comparison flags until CAPTURE_TAIL copies
    ; them; the observed branch is not a caller-provided expected receipt.
    jnz ZfFalse
    mov dword ptr [r10+8], 1
    jmp ZfDone
ZfFalse:
    mov dword ptr [r10+8], 0
ZfDone:
ENDM
CALL_BODY MACRO
    mov rcx, qword ptr [RtTransportActiveCall]
    call RtTransportOwnedBody
ENDM

.code
ALIGN 16
PUBLIC RtTransportBaselineRegister,RtTransportBaselineRegisterPrefixEnd,RtTransportBaselineRegisterEnd
RtTransportBaselineRegister PROC FRAME
    db 048h,089h,05ch,024h,008h
    db 048h,089h,074h,024h,010h
    db 048h,089h,07ch,024h,018h
RtTransportBaselineRegisterPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineRegisterPrefixEnd-RtTransportBaselineRegister-15)
    CAPTURE_TAIL
    sub rsp, 28h
    .allocstack 28h
    .savereg rbx, 30h
    .savereg rsi, 38h
    .savereg rdi, 40h
    .endprolog
    CALL_BODY
    mov rbx, qword ptr [rsp+30h]
    mov rsi, qword ptr [rsp+38h]
    mov rdi, qword ptr [rsp+40h]
    add rsp, 28h
    ret
RtTransportBaselineRegisterEnd LABEL BYTE
RtTransportBaselineRegister ENDP

ALIGN 16
PUBLIC RtTransportBaselineRemove,RtTransportBaselineRemovePrefixEnd,RtTransportBaselineRemoveEnd
RtTransportBaselineRemove PROC FRAME
    db 048h,089h,05ch,024h,008h
    db 057h
    .pushreg rdi
    db 048h,083h,0ech,020h
    .allocstack 20h
    .savereg rbx, 30h
    db 048h,08bh,041h,040h
RtTransportBaselineRemovePrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineRemovePrefixEnd-RtTransportBaselineRemove-14)
    .endprolog
    CAPTURE_TAIL
    CALL_BODY
    mov rbx, qword ptr [rsp+30h]
    add rsp, 20h
    pop rdi
    ret
RtTransportBaselineRemoveEnd LABEL BYTE
RtTransportBaselineRemove ENDP

ALIGN 16
PUBLIC RtTransportBaselineConsume,RtTransportBaselineConsumePrefixEnd,RtTransportBaselineConsumeEnd
RtTransportBaselineConsume PROC FRAME
    db 048h,083h,0ech,028h
    .allocstack 28h
    db 048h,08bh,041h,040h
    db 048h,08bh,0d1h ; mov rdx,rcx: exact fixed encoding
    db 049h,03bh,0c0h ; cmp rax,r8: exact fixed encoding
RtTransportBaselineConsumePrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineConsumePrefixEnd-RtTransportBaselineConsume-14)
    .endprolog
    CAPTURE_ZF
    CAPTURE_TAIL
    CALL_BODY
    add rsp, 28h
    ret
RtTransportBaselineConsumeEnd LABEL BYTE
RtTransportBaselineConsume ENDP

ALIGN 16
PUBLIC RtTransportBaselinePump,RtTransportBaselinePumpPrefixEnd,RtTransportBaselinePumpEnd
RtTransportBaselinePump PROC FRAME
    db 089h,054h,024h,010h
    db 053h
    .pushreg rbx
    db 055h
    .pushreg rbp
    db 057h
    .pushreg rdi
    db 041h,055h
    .pushreg r13
    db 048h,083h,0ech,078h
    .allocstack 78h
    db 08bh,0eah ; mov ebp,edx: exact fixed encoding
RtTransportBaselinePumpPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselinePumpPrefixEnd-RtTransportBaselinePump-15)
    .endprolog
    CAPTURE_TAIL
    CALL_BODY
    add rsp, 78h
    pop r13
    pop rdi
    pop rbp
    pop rbx
    ret
RtTransportBaselinePumpEnd LABEL BYTE
RtTransportBaselinePump ENDP

ALIGN 16
PUBLIC RtTransportBaselineDrain,RtTransportBaselineDrainPrefixEnd,RtTransportBaselineDrainEnd
RtTransportBaselineDrain PROC FRAME
    db 040h,055h
    .pushreg rbp
    db 048h,083h,0ech,030h
    .allocstack 30h
    db 048h,083h,079h,038h,000h
    db 048h,08bh,0e9h ; mov rbp,rcx: exact fixed encoding
RtTransportBaselineDrainPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineDrainPrefixEnd-RtTransportBaselineDrain-14)
    .endprolog
    CAPTURE_ZF
    CAPTURE_TAIL
    CALL_BODY
    add rsp, 30h
    pop rbp
    ret
RtTransportBaselineDrainEnd LABEL BYTE
RtTransportBaselineDrain ENDP

ALIGN 16
PUBLIC RtTransportBaselineCallback,RtTransportBaselineCallbackPrefixEnd,RtTransportBaselineCallbackEnd
RtTransportBaselineCallback PROC FRAME
    db 04ch,08bh,0dch ; mov r11,rsp: exact fixed encoding
    db 048h,081h,0ech,098h,000h,000h,000h
    .allocstack 98h
    db 048h,08bh,0c2h ; mov rax,rdx: exact fixed encoding
    db 048h,039h,04ah,058h
RtTransportBaselineCallbackPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineCallbackPrefixEnd-RtTransportBaselineCallback-17)
    .endprolog
    CAPTURE_ZF
    ; This write is deliberately based on the prefix's live R11. Replacing it
    ; with a continuation pointer would corrupt memory or the independent guard.
    mov r10, 0a135b246c357d468h
    mov qword ptr [r11+20h], r10
    CAPTURE_TAIL
    CALL_BODY
    add rsp, 98h
    ret
RtTransportBaselineCallbackEnd LABEL BYTE
RtTransportBaselineCallback ENDP

ALIGN 16
PUBLIC RtTransportBaselineYield,RtTransportBaselineYieldPrefixEnd,RtTransportBaselineYieldEnd
RtTransportBaselineYield PROC FRAME
    db 048h,089h,05ch,024h,008h
    db 048h,089h,074h,024h,018h
    db 048h,089h,07ch,024h,020h
RtTransportBaselineYieldPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineYieldPrefixEnd-RtTransportBaselineYield-15)
    CAPTURE_TAIL
    sub rsp, 28h
    .allocstack 28h
    .savereg rbx, 30h
    .savereg rsi, 40h
    .savereg rdi, 48h
    .endprolog
    CALL_BODY
    mov rbx, qword ptr [rsp+30h]
    mov rsi, qword ptr [rsp+40h]
    mov rdi, qword ptr [rsp+48h]
    add rsp, 28h
    ret
RtTransportBaselineYieldEnd LABEL BYTE
RtTransportBaselineYield ENDP

ALIGN 16
PUBLIC RtTransportBaselineDispatch,RtTransportBaselineDispatchPrefixEnd,RtTransportBaselineDispatchEnd
RtTransportBaselineDispatch PROC FRAME
    db 040h,057h
    .pushreg rdi
    db 048h,081h,0ech,050h,001h,000h,000h
    .allocstack 150h
    db 048h,083h,079h,010h,000h
RtTransportBaselineDispatchPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineDispatchPrefixEnd-RtTransportBaselineDispatch-14)
    .endprolog
    CAPTURE_ZF
    CAPTURE_TAIL
    CALL_BODY
    add rsp, 150h
    pop rdi
    ret
RtTransportBaselineDispatchEnd LABEL BYTE
RtTransportBaselineDispatch ENDP

ALIGN 16
PUBLIC RtTransportBaselineBatch,RtTransportBaselineBatchPrefixEnd,RtTransportBaselineBatchEnd
RtTransportBaselineBatch PROC FRAME
    db 048h,089h,05ch,024h,018h
    db 056h
    .pushreg rsi
    db 041h,056h
    .pushreg r14
    db 041h,057h
    .pushreg r15
    db 048h,083h,0ech,040h
    .allocstack 40h
    .savereg rbx, 70h
RtTransportBaselineBatchPrefixEnd LABEL BYTE
    .ERRNZ (RtTransportBaselineBatchPrefixEnd-RtTransportBaselineBatch-14)
    .endprolog
    CAPTURE_TAIL
    CALL_BODY
    mov rbx, qword ptr [rsp+70h]
    add rsp, 40h
    pop r15
    pop r14
    pop rsi
    ret
RtTransportBaselineBatchEnd LABEL BYTE
RtTransportBaselineBatch ENDP

ALIGN 16
PUBLIC RtTransportInvoke,RtTransportInvokeReturn
RtTransportInvoke PROC FRAME
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
    sub rsp, 88h
    .allocstack 88h
    .endprolog
    mov qword ptr [rsp+50h], r8
    mov qword ptr [rsp+58h], rcx
    mov qword ptr [rsp+60h], rdx
    mov qword ptr [r8+136], rsp
    mov rax, 0d125e236f347a458h
    mov qword ptr [rsp+40h], rax
    mov qword ptr [r8+152], rax
    mov rbx, 01122334455667788h
    mov rbp, 02233445566778899h
    mov rsi, 033445566778899aah
    mov rdi, 0445566778899aabbh
    mov r12, 05566778899aabbcch
    mov r13, 066778899aabbccddh
    mov r14, 0778899aabbccddeeh
    mov r15, 08899aabbccddeeffh
    mov r10, qword ptr [rsp+60h]
    mov rcx, qword ptr [r10]
    mov rdx, qword ptr [r10+8]
    mov r8, qword ptr [r10+16]
    mov r9, qword ptr [r10+24]
    mov rax, 09135792468abcdefh
    mov r11, 08246813579abcdefh
    cmp dword ptr [RtTransportTraceRequested], 0
    je InvokeWithoutTrace
    pushfq
    or qword ptr [rsp], 100h
    popfq
InvokeWithoutTrace:
    call qword ptr [rsp+58h]
RtTransportInvokeReturn LABEL BYTE
    mov r10, qword ptr [rsp+50h]
    mov qword ptr [r10+128], rax
    mov qword ptr [r10+144], rsp
    mov qword ptr [r10+176], rbx
    mov qword ptr [r10+184], rbp
    mov qword ptr [r10+192], rsi
    mov qword ptr [r10+200], rdi
    mov qword ptr [r10+208], r12
    mov qword ptr [r10+216], r13
    mov qword ptr [r10+224], r14
    mov qword ptr [r10+232], r15
    mov r11, qword ptr [rsp+40h]
    mov qword ptr [r10+160], r11
    add rsp, 88h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
RtTransportInvoke ENDP
END
