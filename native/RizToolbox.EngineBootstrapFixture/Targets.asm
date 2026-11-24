OPTION CASEMAP:NONE
EXTERN RizFixedBodyStart:PROC
EXTERN RizFixedBodyExecute:PROC
EXTERN RizFixedBodyRelease:PROC
EXTERN RizFixedBodyDetach:PROC

; Owned targets only: these exact prefix bytes model four fixed native shapes.
; The bodies belong to the fixture DLL. No game function is invoked. Labels
; are exported separately by the root builder/definition file for raw-PC and
; RUNTIME_FUNCTION verification; PUBLIC alone is not a PE export directive.

.const
ALIGN 16
PUBLIC RizFixedOwnedMarker
RizFixedOwnedMarker LABEL BYTE
    db 'RIZFIXEDOWNEDV1', 0

.code
ALIGN 16
PUBLIC RizFixedOwnedStart
PUBLIC RizFixedOwnedStartPrefixEnd
PUBLIC RizFixedOwnedStartPrologEnd
PUBLIC RizFixedOwnedStartBodyReturn
PUBLIC RizFixedOwnedStartEnd
RizFixedOwnedStart PROC FRAME
    mov qword ptr [rsp+08h], rbx
    mov qword ptr [rsp+18h], rsi
    mov qword ptr [rsp+20h], rdi
RizFixedOwnedStartPrefixEnd LABEL BYTE
    .ERRNZ (RizFixedOwnedStartPrefixEnd - RizFixedOwnedStart - 15)
    sub rsp, 28h
    .allocstack 28h
    ; The actual stores occurred above, before the allocation. Until this PC
    ; RBX/RSI/RDI retain their original values. Attribute saves only when the
    ; fixed frame exists, using its final-RSP-relative home-slot offsets.
    .savereg rbx, 30h
    .savereg rsi, 40h
    .savereg rdi, 48h
    .endprolog
RizFixedOwnedStartPrologEnd LABEL BYTE
    ; Exercise all three saved registers, while preserving four typed args.
    mov rbx, rcx
    mov rsi, rdx
    mov rdi, r8
    call RizFixedBodyStart
RizFixedOwnedStartBodyReturn LABEL BYTE
    mov rbx, qword ptr [rsp+30h]
    mov rsi, qword ptr [rsp+40h]
    mov rdi, qword ptr [rsp+48h]
    add rsp, 28h
    ret
RizFixedOwnedStartEnd LABEL BYTE
RizFixedOwnedStart ENDP

ALIGN 16
PUBLIC RizFixedOwnedExecute
PUBLIC RizFixedOwnedExecutePrefixEnd
PUBLIC RizFixedOwnedExecutePrologEnd
PUBLIC RizFixedOwnedExecuteBodyReturn
PUBLIC RizFixedOwnedExecuteEnd
RizFixedOwnedExecute PROC FRAME
    db 040h, 055h
    .pushreg rbp
    push rbx
    .pushreg rbx
    push rdi
    .pushreg rdi
    push r14
    .pushreg r14
    lea rbp, [rsp-78h]
    sub rsp, 178h
    .allocstack 178h
RizFixedOwnedExecutePrefixEnd LABEL BYTE
    .ERRNZ (RizFixedOwnedExecutePrefixEnd - RizFixedOwnedExecute - 18)
    .endprolog
RizFixedOwnedExecutePrologEnd LABEL BYTE
    mov rbx, rcx
    mov rdi, rdx
    mov r14, rcx
    call RizFixedBodyExecute
RizFixedOwnedExecuteBodyReturn LABEL BYTE
    add rsp, 178h
    pop r14
    pop rdi
    pop rbx
    pop rbp
    ret
RizFixedOwnedExecuteEnd LABEL BYTE
RizFixedOwnedExecute ENDP

ALIGN 16
PUBLIC RizFixedOwnedRelease
PUBLIC RizFixedOwnedReleasePrefixEnd
PUBLIC RizFixedOwnedReleasePrologEnd
PUBLIC RizFixedOwnedReleaseBodyReturn
PUBLIC RizFixedOwnedReleaseEnd
RizFixedOwnedRelease PROC FRAME
    db 040h, 053h
    .pushreg rbx
    sub rsp, 20h
    .allocstack 20h
    dec dword ptr [rcx+60h]
    db 048h, 08bh, 0d9h ; mov rbx, rcx: exact fixed register-MOV encoding
    cmp dword ptr [rcx+60h], 0
RizFixedOwnedReleasePrefixEnd LABEL BYTE
    .ERRNZ (RizFixedOwnedReleasePrefixEnd - RizFixedOwnedRelease - 16)
    .endprolog
RizFixedOwnedReleasePrologEnd LABEL BYTE
    ; First real continuation instruction consumes the prefix CMP flags.
    ; Native entry is still uint8(node); EDX/R8D are fixture diagnostics.
    setz r8b
    movzx r8d, r8b
    mov edx, dword ptr [rbx+60h]
    mov rcx, rbx
    call RizFixedBodyRelease
RizFixedOwnedReleaseBodyReturn LABEL BYTE
    add rsp, 20h
    pop rbx
    ret
RizFixedOwnedReleaseEnd LABEL BYTE
RizFixedOwnedRelease ENDP

ALIGN 16
PUBLIC RizFixedOwnedDetach
PUBLIC RizFixedOwnedDetachPrefixEnd
PUBLIC RizFixedOwnedDetachPrologEnd
PUBLIC RizFixedOwnedDetachBodyReturn
PUBLIC RizFixedOwnedDetachEnd
RizFixedOwnedDetach PROC FRAME
    mov qword ptr [rsp+20h], rbp
    push rdi
    .pushreg rdi
    sub rsp, 20h
    .allocstack 20h
    .savereg rbp, 48h
    db 048h, 08bh, 0f9h ; mov rdi, rcx
    db 033h, 0edh       ; xor ebp, ebp
RizFixedOwnedDetachPrefixEnd LABEL BYTE
    .ERRNZ (RizFixedOwnedDetachPrefixEnd - RizFixedOwnedDetach - 15)
    .endprolog
RizFixedOwnedDetachPrologEnd LABEL BYTE
    mov edx, ebp
    mov rcx, rdi
    call RizFixedBodyDetach
RizFixedOwnedDetachBodyReturn LABEL BYTE
    mov rbp, qword ptr [rsp+48h]
    add rsp, 20h
    pop rdi
    ret
RizFixedOwnedDetachEnd LABEL BYTE
RizFixedOwnedDetach ENDP
END
