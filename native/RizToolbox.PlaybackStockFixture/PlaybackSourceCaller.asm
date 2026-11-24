option casemap:none
PUBLIC StockFixtureMachineInvoke
.const
ALIGN 16
source_xmm_pattern dq 0123456789abcdefh,0fedcba9876543210h
.code
StockFixtureMachineInvoke PROC FRAME
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
    sub rsp,0f8h
    .allocstack 0f8h
    movaps xmmword ptr [rsp+40h],xmm6
    .savexmm128 xmm6,40h
    movaps xmmword ptr [rsp+50h],xmm7
    .savexmm128 xmm7,50h
    movaps xmmword ptr [rsp+60h],xmm8
    .savexmm128 xmm8,60h
    movaps xmmword ptr [rsp+70h],xmm9
    .savexmm128 xmm9,70h
    movaps xmmword ptr [rsp+80h],xmm10
    .savexmm128 xmm10,80h
    movaps xmmword ptr [rsp+90h],xmm11
    .savexmm128 xmm11,90h
    movaps xmmword ptr [rsp+0a0h],xmm12
    .savexmm128 xmm12,0a0h
    movaps xmmword ptr [rsp+0b0h],xmm13
    .savexmm128 xmm13,0b0h
    movaps xmmword ptr [rsp+0c0h],xmm14
    .savexmm128 xmm14,0c0h
    movaps xmmword ptr [rsp+0d0h],xmm15
    .savexmm128 xmm15,0d0h
    .endprolog
    mov dword ptr [rsp+0f0h],ecx
    mov qword ptr [rsp+20h],rdx
    mov qword ptr [rsp+28h],r8
    mov qword ptr [rsp+30h],r9
    mov rax,qword ptr [rsp+160h]
    mov qword ptr [rsp+38h],rax
    mov rax,qword ptr [rsp+168h]
    mov qword ptr [rsp+0e0h],rax
    mov rax,qword ptr [rsp+170h]
    mov qword ptr [rsp+0e8h],rax
    mov rbx,01111222233334444h
    mov rbp,05555666677778888h
    mov rsi,09999aaaabbbbcccch
    mov rdi,0ddddeeeeffff0000h
    mov r12,0123456789abcdef0h
    mov r13,0fedcba9876543210h
    mov r14,013572468abcdef00h
    mov r15,00f0e0d0c0b0a0908h
    movdqa xmm6,xmmword ptr [source_xmm_pattern]
    movdqa xmm7,xmm6
    movdqa xmm8,xmm6
    movdqa xmm9,xmm6
    movdqa xmm10,xmm6
    movdqa xmm11,xmm6
    movdqa xmm12,xmm6
    movdqa xmm13,xmm6
    movdqa xmm14,xmm6
    movdqa xmm15,xmm6
    mov r10,qword ptr [rsp+0e0h]
    call snapshot
    mov eax,dword ptr [rsp+0f0h]
    cmp eax,7
    je void_arguments
    cmp eax,1
    je void_arguments
    cmp eax,2
    je single_arguments
    cmp eax,3
    je double_arguments
    mov rdx,qword ptr [rsp+30h]
    jmp scalar_arguments
single_arguments:
    movss xmm1,dword ptr [rsp+30h]
    jmp scalar_arguments
double_arguments:
    movsd xmm1,qword ptr [rsp+30h]
scalar_arguments:
    mov r8,qword ptr [rsp+38h]
    jmp invoke_target
void_arguments:
    mov rdx,qword ptr [rsp+38h]
invoke_target:
    mov rcx,qword ptr [rsp+28h]
    call qword ptr [rsp+20h]
    mov r10,qword ptr [rsp+0e8h]
    call snapshot
    movaps xmm6,xmmword ptr [rsp+40h]
    movaps xmm7,xmmword ptr [rsp+50h]
    movaps xmm8,xmmword ptr [rsp+60h]
    movaps xmm9,xmmword ptr [rsp+70h]
    movaps xmm10,xmmword ptr [rsp+80h]
    movaps xmm11,xmmword ptr [rsp+90h]
    movaps xmm12,xmmword ptr [rsp+0a0h]
    movaps xmm13,xmmword ptr [rsp+0b0h]
    movaps xmm14,xmmword ptr [rsp+0c0h]
    movaps xmm15,xmmword ptr [rsp+0d0h]
    add rsp,0f8h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
snapshot:
    mov qword ptr [r10+00h],rbx
    mov qword ptr [r10+08h],rbp
    mov qword ptr [r10+10h],rsi
    mov qword ptr [r10+18h],rdi
    mov qword ptr [r10+20h],r12
    mov qword ptr [r10+28h],r13
    mov qword ptr [r10+30h],r14
    mov qword ptr [r10+38h],r15
    pushfq
    pop r11
    mov qword ptr [r10+40h],r11
    movdqu xmmword ptr [r10+50h],xmm6
    movdqu xmmword ptr [r10+60h],xmm7
    movdqu xmmword ptr [r10+70h],xmm8
    movdqu xmmword ptr [r10+80h],xmm9
    movdqu xmmword ptr [r10+90h],xmm10
    movdqu xmmword ptr [r10+0a0h],xmm11
    movdqu xmmword ptr [r10+0b0h],xmm12
    movdqu xmmword ptr [r10+0c0h],xmm13
    movdqu xmmword ptr [r10+0d0h],xmm14
    movdqu xmmword ptr [r10+0e0h],xmm15
    ret
StockFixtureMachineInvoke ENDP
END
