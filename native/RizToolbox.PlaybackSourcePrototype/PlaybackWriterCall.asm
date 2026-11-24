option casemap:none
PUBLIC RtPlaybackWriterCall, RtPlaybackWriterCallReturned
PUBLIC RtPlaybackLowerPitchWrapperCall, RtPlaybackLowerPitchWrapperCallReturned
PUBLIC RtPlaybackLowerTimeWrapperCall, RtPlaybackLowerTimeWrapperCallReturned
.code
RtPlaybackWriterCall PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rax,rcx
    mov rcx,rdx
    movd xmm1,r8d
    mov r8,r9
    call rax
RtPlaybackWriterCallReturned LABEL BYTE
    add rsp,28h
    ret
RtPlaybackWriterCall ENDP
; The pinned GameAssembly wrapper restores its stack and tail-jumps the icall.
; Therefore the lower entry must see THIS actual CALL return label, rather than
; a fabricated return RVA inside GameAssembly. Parameters are original/self/bits/MI.
RtPlaybackLowerPitchWrapperCall PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rax,rcx
    mov rcx,rdx
    movd xmm1,r8d
    mov r8,r9
    call rax
RtPlaybackLowerPitchWrapperCallReturned LABEL BYTE
    add rsp,28h
    ret
RtPlaybackLowerPitchWrapperCall ENDP
RtPlaybackLowerTimeWrapperCall PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rax,rcx
    mov rcx,rdx
    movd xmm1,r8d
    mov r8,r9
    call rax
RtPlaybackLowerTimeWrapperCallReturned LABEL BYTE
    add rsp,28h
    ret
RtPlaybackLowerTimeWrapperCall ENDP
END
