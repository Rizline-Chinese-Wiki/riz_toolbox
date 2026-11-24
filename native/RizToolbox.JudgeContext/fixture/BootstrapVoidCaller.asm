OPTION CASEMAP:NONE
EXTERN RtBootstrapBeforeMid:PROC
EXTERN RtBootstrapGetList:PROC
EXTERN RtBootstrapBody:PROC

BOOTSTRAP_TARGET MACRO target:req, site:req, continuation:req
    ALIGN 16
    PUBLIC target
    PUBLIC site
    PUBLIC continuation
    target PROC FRAME
        REPT 32
            nop
        ENDM
        push rbp
        .pushreg rbp
        push rsi
        .pushreg rsi
        sub rsp, 38h
        .allocstack 38h
        .endprolog
        mov [rsp+20h], rcx
        mov [rsp+28h], rdx
        call RtBootstrapBeforeMid
        mov rcx, [rsp+20h]
        call RtBootstrapGetList
        mov rbp, rax
        mov rsi, [rsp+20h]
    site LABEL BYTE
        db 00fh, 01fh, 044h, 000h, 000h
    continuation LABEL BYTE
        mov rcx, [rsp+20h]
        mov rdx, [rsp+28h]
        call RtBootstrapBody
        add rsp, 38h
        pop rsi
        pop rbp
        ret
    target ENDP
ENDM
.code
BOOTSTRAP_TARGET RtBootstrapTarget0, RtBootstrapMidSite0, RtBootstrapMidReturn0
BOOTSTRAP_TARGET RtBootstrapTarget1, RtBootstrapMidSite1, RtBootstrapMidReturn1

PUBLIC RtBootstrapCall
PUBLIC RtBootstrapCallerReturn
; void(target RCX, self RDX, MethodInfo R8). The return label is the actual
; native caller observed through Dobby and the permanent parent transport.
RtBootstrapCall PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    call rax
RtBootstrapCallerReturn LABEL BYTE
    add rsp, 28h
    ret
RtBootstrapCall ENDP
END
