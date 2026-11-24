OPTION CASEMAP:NONE
EXTERN RizEngineTransportContinuation:QWORD

; Nine static originals for UnityPlayer SHA256
; 43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1.
; Each DB group is one actual source instruction, read from the fixed disk PE.
; The source continuation is entry+PrefixEnd, not a copied/relocated CALL.
; Nothing here patches a source, allocates memory, reads LastError, invokes an
; observer, normalizes a return value, or grants startup/cleanup authority.
;
; The tail preserves RFLAGS, RAX and the four actual integer arguments as they
; stand after the fixed prefix. Its scratch is R11 except Callback's R10:
; - Register/Remove/Consume/Pump/Drain/Dispatch/Batch full fixed bodies have no
;   R11/R11D/R11W/R11B accesses at all.
; - Yield's only R11 uses are its 79012E epilog LEA definition followed by four
;   reads. No indirect jump or direct jump into the read region bypasses LEA.
; - Callback needs its entry R11=original RSP for stores through 78F64C; its
;   complete 78F5E0..78F673 body has no R10 alias access, so use R10 here.
; These are fixed typed-ABI facts, not general all-register trampolines.
;
; Do not substitute FF25 memory-indirect tails: the preceding four-original
; fixture found that Windows can classify that jump PC as an epilog despite
; SizeOfProlog. MOV + ModRM11 JMP keeps the chosen safe tail shape; actual new
; RtlVirtualUnwind/exception verification is still required at every PC.
; The tail belongs to the recorded prolog and RF; there is no inline pointer.

.code
ALIGN 16
PUBLIC RizEngineTransportOriginalRegister
PUBLIC RizEngineTransportOriginalRegisterPrefixEnd
PUBLIC RizEngineTransportOriginalRegisterEnd
RizEngineTransportOriginalRegister PROC FRAME
    ; 44D630: mov [rsp+08h],rbx / mov [rsp+10h],rsi / mov [rsp+18h],rdi.
    ; No later source PUSH/SUB has executed in this short original RF.
    db 048h,089h,05ch,024h,008h
    .savereg rbx, 08h
    db 048h,089h,074h,024h,010h
    .savereg rsi, 10h
    db 048h,089h,07ch,024h,018h
    .savereg rdi, 18h
RizEngineTransportOriginalRegisterPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalRegisterPrefixEnd - RizEngineTransportOriginalRegister - 15)
    mov r11, qword ptr [RizEngineTransportContinuation+0*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalRegisterEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalRegisterEnd - RizEngineTransportOriginalRegisterPrefixEnd - 10)
RizEngineTransportOriginalRegister ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalRemove
PUBLIC RizEngineTransportOriginalRemovePrefixEnd
PUBLIC RizEngineTransportOriginalRemoveEnd
RizEngineTransportOriginalRemove PROC FRAME
    ; 447AD0: mov [rsp+08h],rbx / push rdi / sub rsp,20h / mov rax,[rcx+40h].
    db 048h,089h,05ch,024h,008h
    db 057h
    .pushreg rdi
    db 048h,083h,0ech,020h
    .allocstack 20h
    ; As in the source RF, defer the home save until this allocation exists.
    ; RBX is unchanged at all earlier PCs; final RSP+30h is entry RSP+08h.
    .savereg rbx, 30h
    db 048h,08bh,041h,040h
RizEngineTransportOriginalRemovePrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalRemovePrefixEnd - RizEngineTransportOriginalRemove - 14)
    mov r11, qword ptr [RizEngineTransportContinuation+1*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalRemoveEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalRemoveEnd - RizEngineTransportOriginalRemovePrefixEnd - 10)
RizEngineTransportOriginalRemove ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalConsume
PUBLIC RizEngineTransportOriginalConsumePrefixEnd
PUBLIC RizEngineTransportOriginalConsumeEnd
RizEngineTransportOriginalConsume PROC FRAME
    ; 447C30: sub rsp,28h / mov rax,[rcx+40h] / mov rdx,rcx / cmp rax,r8.
    db 048h,083h,0ech,028h
    .allocstack 28h
    db 048h,08bh,041h,040h
    db 048h,08bh,0d1h
    db 049h,03bh,0c0h
RizEngineTransportOriginalConsumePrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalConsumePrefixEnd - RizEngineTransportOriginalConsume - 14)
    mov r11, qword ptr [RizEngineTransportContinuation+2*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalConsumeEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalConsumeEnd - RizEngineTransportOriginalConsumePrefixEnd - 10)
RizEngineTransportOriginalConsume ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalPump
PUBLIC RizEngineTransportOriginalPumpPrefixEnd
PUBLIC RizEngineTransportOriginalPumpEnd
RizEngineTransportOriginalPump PROC FRAME
    ; 447D60: mov [rsp+10h],edx / push rbx,rbp,rdi,r13 / sub rsp,78h / mov ebp,edx.
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
    db 08bh,0eah
    ; Source XMM6's save starts at 447D6F, outside this replay; no SAVEXMM128.
RizEngineTransportOriginalPumpPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalPumpPrefixEnd - RizEngineTransportOriginalPump - 15)
    mov r11, qword ptr [RizEngineTransportContinuation+3*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalPumpEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalPumpEnd - RizEngineTransportOriginalPumpPrefixEnd - 10)
RizEngineTransportOriginalPump ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalDrain
PUBLIC RizEngineTransportOriginalDrainPrefixEnd
PUBLIC RizEngineTransportOriginalDrainEnd
RizEngineTransportOriginalDrain PROC FRAME
    ; 447750: redundant REX push rbp / sub rsp,30h / cmp [rcx+38h],0 / mov rbp,rcx.
    db 040h,055h
    .pushreg rbp
    db 048h,083h,0ech,030h
    .allocstack 30h
    db 048h,083h,079h,038h,000h
    db 048h,08bh,0e9h
RizEngineTransportOriginalDrainPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalDrainPrefixEnd - RizEngineTransportOriginalDrain - 14)
    mov r11, qword ptr [RizEngineTransportContinuation+4*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalDrainEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalDrainEnd - RizEngineTransportOriginalDrainPrefixEnd - 10)
RizEngineTransportOriginalDrain ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalCallback
PUBLIC RizEngineTransportOriginalCallbackPrefixEnd
PUBLIC RizEngineTransportOriginalCallbackEnd
RizEngineTransportOriginalCallback PROC FRAME
    ; 78F5E0: mov r11,rsp / sub rsp,98h / mov rax,rdx / cmp [rdx+58h],rcx.
    db 04ch,08bh,0dch
    ; Volatile R11 is an original stack-base temporary, not a nonvolatile frame
    ; register. In particular, do not emit UWOP_SET_FPREG for the entry MOV.
    db 048h,081h,0ech,098h,000h,000h,000h
    .allocstack 98h
    db 048h,08bh,0c2h
    db 048h,039h,04ah,058h
RizEngineTransportOriginalCallbackPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalCallbackPrefixEnd - RizEngineTransportOriginalCallback - 17)
    mov r10, qword ptr [RizEngineTransportContinuation+5*8]
    jmp r10
    .endprolog
RizEngineTransportOriginalCallbackEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalCallbackEnd - RizEngineTransportOriginalCallbackPrefixEnd - 10)
RizEngineTransportOriginalCallback ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalYield
PUBLIC RizEngineTransportOriginalYieldPrefixEnd
PUBLIC RizEngineTransportOriginalYieldEnd
RizEngineTransportOriginalYield PROC FRAME
    ; 78FC50: mov [rsp+08h],rbx / mov [rsp+18h],rsi / mov [rsp+20h],rdi.
    db 048h,089h,05ch,024h,008h
    .savereg rbx, 08h
    db 048h,089h,074h,024h,018h
    .savereg rsi, 18h
    db 048h,089h,07ch,024h,020h
    .savereg rdi, 20h
RizEngineTransportOriginalYieldPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalYieldPrefixEnd - RizEngineTransportOriginalYield - 15)
    mov r11, qword ptr [RizEngineTransportContinuation+6*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalYieldEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalYieldEnd - RizEngineTransportOriginalYieldPrefixEnd - 10)
RizEngineTransportOriginalYield ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalDispatch
PUBLIC RizEngineTransportOriginalDispatchPrefixEnd
PUBLIC RizEngineTransportOriginalDispatchEnd
RizEngineTransportOriginalDispatch PROC FRAME
    ; 5A5260: redundant REX push rdi / sub rsp,150h / cmp [rcx+10h],0.
    db 040h,057h
    .pushreg rdi
    db 048h,081h,0ech,050h,001h,000h,000h
    .allocstack 150h
    db 048h,083h,079h,010h,000h
RizEngineTransportOriginalDispatchPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalDispatchPrefixEnd - RizEngineTransportOriginalDispatch - 14)
    mov r11, qword ptr [RizEngineTransportContinuation+7*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalDispatchEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalDispatchEnd - RizEngineTransportOriginalDispatchPrefixEnd - 10)
RizEngineTransportOriginalDispatch ENDP

ALIGN 16
PUBLIC RizEngineTransportOriginalBatch
PUBLIC RizEngineTransportOriginalBatchPrefixEnd
PUBLIC RizEngineTransportOriginalBatchEnd
RizEngineTransportOriginalBatch PROC FRAME
    ; 5DA510: mov [rsp+18h],rbx / push rsi,r14,r15 / sub rsp,40h.
    db 048h,089h,05ch,024h,018h
    db 056h
    .pushreg rsi
    db 041h,056h
    .pushreg r14
    db 041h,057h
    .pushreg r15
    db 048h,083h,0ech,040h
    .allocstack 40h
    ; As in the source RF, register the earlier save at final RSP+70h only
    ; after all three pushes and the allocation; RBX has not changed before it.
    .savereg rbx, 70h
RizEngineTransportOriginalBatchPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalBatchPrefixEnd - RizEngineTransportOriginalBatch - 14)
    mov r11, qword ptr [RizEngineTransportContinuation+8*8]
    jmp r11
    .endprolog
RizEngineTransportOriginalBatchEnd LABEL BYTE
    .ERRNZ (RizEngineTransportOriginalBatchEnd - RizEngineTransportOriginalBatchPrefixEnd - 10)
RizEngineTransportOriginalBatch ENDP
END
