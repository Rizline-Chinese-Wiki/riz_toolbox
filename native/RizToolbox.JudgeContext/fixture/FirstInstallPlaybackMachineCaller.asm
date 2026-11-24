option casemap:none
PUBLIC RizFirstInstallPlayback_TraceOriginal
PUBLIC RizFirstInstallPlayback_TraceReturned
.code
RizFirstInstallPlayback_TraceOriginal PROC FRAME
    sub rsp,128h
    .allocstack 128h
    .endprolog
    mov [rsp+20h],rbx
    mov [rsp+28h],rbp
    mov [rsp+30h],rsi
    mov [rsp+38h],rdi
    mov [rsp+40h],r12
    mov [rsp+48h],r13
    mov [rsp+50h],r14
    mov [rsp+58h],r15
    movdqu [rsp+60h],xmm6
    movdqu [rsp+70h],xmm7
    movdqu [rsp+80h],xmm8
    movdqu [rsp+90h],xmm9
    movdqu [rsp+0a0h],xmm10
    movdqu [rsp+0b0h],xmm11
    movdqu [rsp+0c0h],xmm12
    movdqu [rsp+0d0h],xmm13
    movdqu [rsp+0e0h],xmm14
    movdqu [rsp+0f0h],xmm15
    mov rax,rcx
    mov rcx,rdx
    test r8d,r8d
    jz invoke_original
    pushfq
    or qword ptr [rsp],100h
    popfq
invoke_original:
    call rax
RizFirstInstallPlayback_TraceReturned LABEL BYTE
    mov [rsp+100h],eax
    cmp rbx,[rsp+20h]
    jne broken_nonvolatile
    cmp rbp,[rsp+28h]
    jne broken_nonvolatile
    cmp rsi,[rsp+30h]
    jne broken_nonvolatile
    cmp rdi,[rsp+38h]
    jne broken_nonvolatile
    cmp r12,[rsp+40h]
    jne broken_nonvolatile
    cmp r13,[rsp+48h]
    jne broken_nonvolatile
    cmp r14,[rsp+50h]
    jne broken_nonvolatile
    cmp r15,[rsp+58h]
    jne broken_nonvolatile
    movdqu xmm0,[rsp+60h]
    pcmpeqb xmm0,xmm6
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+70h]
    pcmpeqb xmm0,xmm7
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+80h]
    pcmpeqb xmm0,xmm8
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+90h]
    pcmpeqb xmm0,xmm9
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0a0h]
    pcmpeqb xmm0,xmm10
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0b0h]
    pcmpeqb xmm0,xmm11
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0c0h]
    pcmpeqb xmm0,xmm12
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0d0h]
    pcmpeqb xmm0,xmm13
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0e0h]
    pcmpeqb xmm0,xmm14
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    movdqu xmm0,[rsp+0f0h]
    pcmpeqb xmm0,xmm15
    pmovmskb r10d,xmm0
    cmp r10d,0ffffh
    jne broken_nonvolatile
    jmp finish
broken_nonvolatile:
    mov dword ptr [rsp+100h],-19
finish:
    mov rbx,[rsp+20h]
    mov rbp,[rsp+28h]
    mov rsi,[rsp+30h]
    mov rdi,[rsp+38h]
    mov r12,[rsp+40h]
    mov r13,[rsp+48h]
    mov r14,[rsp+50h]
    mov r15,[rsp+58h]
    movdqu xmm6,[rsp+60h]
    movdqu xmm7,[rsp+70h]
    movdqu xmm8,[rsp+80h]
    movdqu xmm9,[rsp+90h]
    movdqu xmm10,[rsp+0a0h]
    movdqu xmm11,[rsp+0b0h]
    movdqu xmm12,[rsp+0c0h]
    movdqu xmm13,[rsp+0d0h]
    movdqu xmm14,[rsp+0e0h]
    movdqu xmm15,[rsp+0f0h]
    mov eax,[rsp+100h]
    add rsp,128h
    ret
RizFirstInstallPlayback_TraceOriginal ENDP
END
