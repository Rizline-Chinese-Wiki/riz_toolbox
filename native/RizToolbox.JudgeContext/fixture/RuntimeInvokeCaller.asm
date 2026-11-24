OPTION CASEMAP:NONE
EXTERN RtRuntimeInvokeBody:PROC
.code
ALIGN 16
PUBLIC RtRuntimeInvokeTarget
; Dobby steals only the unused NOP prefix; the original four arguments are
; passed unchanged through its trampoline and this native tail continuation.
RtRuntimeInvokeTarget PROC
    REPT 32
        nop
    ENDM
    jmp RtRuntimeInvokeBody
RtRuntimeInvokeTarget ENDP

PUBLIC RtRuntimeInvokeCall
PUBLIC RtRuntimeInvokeCallerReturn
; void* (target RCX, method RDX, self R8, arguments R9, exception [rsp+28h]).
RtRuntimeInvokeCall PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    mov r8, r9
    mov r9, [rsp+50h]
    call rax
RtRuntimeInvokeCallerReturn LABEL BYTE
    add rsp, 28h
    ret
RtRuntimeInvokeCall ENDP
END
