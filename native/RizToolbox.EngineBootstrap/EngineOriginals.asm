OPTION CASEMAP:NONE
EXTERN RizEngineContinuation:QWORD

; Static originals for the four *fixed* Windows x64 prefix shapes. Nothing in
; this file allocates, decodes instructions, copies executable text, or calls
; a loader/CRT service. Each continuation is configured once before publishing
; its corresponding fixed target. The MOV/JMP preserves RFLAGS and clobbers
; only volatile R11. This is a four-typed-ABI contract, not full-GPR replay.
;
; An earlier FF25 candidate failed actual RtlVirtualUnwind at its jump PC:
; Windows treated that instruction as an epilog even inside SizeOfProlog.
; Load the continuation into R11 and jump via register (ModRM11), rather than
; using an epilog-shaped memory-indirect jump. Both MOV-PC and JMP-PC still
; require actual per-PC unwind verification. PrefixEnd is the MOV's first
; byte; End is the exclusive RF/prolog bound, with no inline pointer.

.code
ALIGN 16
PUBLIC RizEngineOriginalStart
PUBLIC RizEngineOriginalStartPrefixEnd
PUBLIC RizEngineOriginalStartEnd
RizEngineOriginalStart PROC FRAME
    mov qword ptr [rsp+08h], rbx
    .savereg rbx, 08h
    mov qword ptr [rsp+18h], rsi
    .savereg rsi, 18h
    mov qword ptr [rsp+20h], rdi
    .savereg rdi, 20h
RizEngineOriginalStartPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalStartPrefixEnd - RizEngineOriginalStart - 15)
    mov r11, qword ptr [RizEngineContinuation]
    jmp r11
    .endprolog
RizEngineOriginalStartEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalStartEnd - RizEngineOriginalStartPrefixEnd - 10)
RizEngineOriginalStart ENDP

ALIGN 16
PUBLIC RizEngineOriginalExecute
PUBLIC RizEngineOriginalExecutePrefixEnd
PUBLIC RizEngineOriginalExecuteEnd
RizEngineOriginalExecute PROC FRAME
    ; Force the real redundant REX byte. Plain PUSH would be one byte shorter.
    db 040h, 055h
    .pushreg rbp
    push rbx
    .pushreg rbx
    push rdi
    .pushreg rdi
    push r14
    .pushreg r14
    lea rbp, [rsp-78h]
    ; This is a saved nonvolatile temporary, not a declared frame pointer.
    ; In particular there must be no UWOP_SET_FPREG for this LEA.
    sub rsp, 178h
    .allocstack 178h
RizEngineOriginalExecutePrefixEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalExecutePrefixEnd - RizEngineOriginalExecute - 18)
    mov r11, qword ptr [RizEngineContinuation+8]
    jmp r11
    .endprolog
RizEngineOriginalExecuteEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalExecuteEnd - RizEngineOriginalExecutePrefixEnd - 10)
RizEngineOriginalExecute ENDP

ALIGN 16
PUBLIC RizEngineOriginalRelease
PUBLIC RizEngineOriginalReleasePrefixEnd
PUBLIC RizEngineOriginalReleaseEnd
RizEngineOriginalRelease PROC FRAME
    db 040h, 053h
    .pushreg rbx
    sub rsp, 20h
    .allocstack 20h
    dec dword ptr [rcx+60h]
    db 048h, 08bh, 0d9h ; mov rbx, rcx: exact fixed register-MOV encoding
    cmp dword ptr [rcx+60h], 0
RizEngineOriginalReleasePrefixEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalReleasePrefixEnd - RizEngineOriginalRelease - 16)
    ; Exactly one DEC above. No instruction may change CMP's flags before
    ; the owned target continuation first consumes ZF.
    mov r11, qword ptr [RizEngineContinuation+16]
    jmp r11
    .endprolog
RizEngineOriginalReleaseEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalReleaseEnd - RizEngineOriginalReleasePrefixEnd - 10)
RizEngineOriginalRelease ENDP

ALIGN 16
PUBLIC RizEngineOriginalDetach
PUBLIC RizEngineOriginalDetachPrefixEnd
PUBLIC RizEngineOriginalDetachEnd
RizEngineOriginalDetach PROC FRAME
    mov qword ptr [rsp+20h], rbp
    push rdi
    .pushreg rdi
    sub rsp, 20h
    .allocstack 20h
    ; Like the fixed real RF, attribute the earlier home-slot save only after
    ; the fixed allocation exists. RBP is unchanged at every earlier PC.
    .savereg rbp, 48h
    db 048h, 08bh, 0f9h ; mov rdi, rcx
    db 033h, 0edh       ; xor ebp, ebp
RizEngineOriginalDetachPrefixEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalDetachPrefixEnd - RizEngineOriginalDetach - 15)
    mov r11, qword ptr [RizEngineContinuation+24]
    jmp r11
    .endprolog
RizEngineOriginalDetachEnd LABEL BYTE
    .ERRNZ (RizEngineOriginalDetachEnd - RizEngineOriginalDetachPrefixEnd - 10)
RizEngineOriginalDetach ENDP
END
