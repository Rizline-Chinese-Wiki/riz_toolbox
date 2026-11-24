OPTION CASEMAP:NONE
EXTERN RtLfBefore:PROC
EXTERN RtLfLoopCheck:PROC
EXTERN RtLfAfter:PROC
EXTERN RtLfDirectBody:PROC
EXTERN RtLfVirtualBody:PROC

; Each target has a real 32-NOP parent patch region, a source loop whose live
; nonvolatiles match the game (RSI event/RBP list/EBX index/RDI invokable), two
; direct CALLs to one callee, and one actual six-byte CALL [RAX+178h].
LISTENER_PARENT MACRO target:req, ending:req, loopName:req, midName:req, midReturn:req, site0:req, site1:req, site2:req, post0:req, post1:req, post2:req
    LOCAL choose1, choose2, finish, snapshotDone
    ALIGN 16
    PUBLIC target, ending, loopName, midName, midReturn, site0, site1, site2, post0, post1, post2
target PROC FRAME
    REPT 32
        nop
    ENDM
    push rbx
    .pushreg rbx
    push rbp
    .pushreg rbp
    push rsi
    .pushreg rsi
    push rdi
    .pushreg rdi
    push r14
    .pushreg r14
    sub rsp, 40h
    .allocstack 40h
    .endprolog
    mov [rsp+20h], rcx
    mov [rsp+28h], rdx
    mov r14, 0aabbccddeeff1122h
    mov [rsp+30h], r14
    mov [rsp+38h], r14
    mov rsi, rcx
    mov rbp, [rsi+08h]
    call RtLfBefore
    xor ebx, ebx
    xor eax, eax
midName LABEL BYTE
    db 00fh, 01fh, 044h, 000h, 000h
midReturn LABEL BYTE
loopName LABEL NEAR
    pushfq
    pop qword ptr [rsi+10h]
    test ebx, ebx
    jz snapshotDone
    call RtLfLoopSnapshot
snapshotDone:
    mov rcx, rsi
    mov edx, ebx
    mov r8, rax
    mov r9, [rsi+10h]
    call RtLfLoopCheck
    cmp ebx, dword ptr [rbp+18h]
    jge finish
    mov rdi, [rbp+10h]
    mov rdi, [rdi+rbx*8]
    cmp dword ptr [rdi+14h], 1
    je choose1
    cmp dword ptr [rdi+14h], 2
    je choose2
    mov rcx, rdi
    xor edx, edx
    mov r8, 088888888789abcdeh
    mov r9, 09999999989abcdefh
    mov rax, 01111111101234567h
    stc
site0 LABEL BYTE
    call RtLfDirect
post0 LABEL BYTE
    inc ebx
    mov eax, ebx
    jmp short loopName
    ; The second branch deliberately has a near JMP, preserving the actual
    ; complete continuation lengths 6 / 9 / 9 instead of a truncated E9.
choose1:
    mov rcx, rdi
    xor edx, edx
    mov r8, 088888888789abcdeh
    mov r9, 09999999989abcdefh
    mov rax, 01111111101234567h
    stc
site1 LABEL BYTE
    call RtLfDirect
post1 LABEL BYTE
    inc ebx
    mov eax, ebx
    jmp near ptr loopName
choose2:
    mov rax, [rdi]
    mov rcx, rdi
    lea rdx, [rsi+18h]
    mov r8, [rax+180h]
    mov r9, 09999999989abcdefh
    stc
site2 LABEL BYTE
    call qword ptr [rax+178h]
post2 LABEL BYTE
    inc ebx
    mov eax, ebx
    jmp near ptr loopName
finish:
    mov rcx, [rsp+20h]
    mov rdx, [rsp+28h]
    mov r8, [rsp+30h]
    mov r9, [rsp+38h]
    call RtLfAfter
    add rsp, 40h
    pop r14
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
.ERRNZ (post0 - site0 - 5)
.ERRNZ (post1 - site1 - 5)
.ERRNZ (post2 - site2 - 6)
.ERRNZ (choose1 - post0 - 6)
.ERRNZ (choose2 - post1 - 9)
.ERRNZ (finish - post2 - 9)
target ENDP
ending LABEL BYTE
ENDM

; Record the actual source machine state immediately at callee entry, before
; any C++ prolog or observer can alter it. Then tail-jump to the C++ body, whose
; real _ReturnAddress() still names the source CALL continuation.
CALLEE_SNAPSHOT MACRO body:req
    mov [rcx+028h], rax
    mov [rcx+030h], rcx
    mov [rcx+038h], rdx
    mov [rcx+040h], rbx
    mov [rcx+048h], rsp
    mov [rcx+050h], rbp
    mov [rcx+058h], rsi
    mov [rcx+060h], rdi
    mov [rcx+068h], r8
    mov [rcx+070h], r9
    mov [rcx+078h], r10
    mov [rcx+080h], r11
    mov [rcx+088h], r12
    mov [rcx+090h], r13
    mov [rcx+098h], r14
    mov [rcx+0a0h], r15
    pushfq
    pop qword ptr [rcx+020h]
    movdqu [rcx+0b0h], xmm0
    movdqu [rcx+0c0h], xmm1
    movdqu [rcx+0d0h], xmm2
    movdqu [rcx+0e0h], xmm3
    movdqu [rcx+0f0h], xmm4
    movdqu [rcx+100h], xmm5
    movdqu [rcx+110h], xmm6
    movdqu [rcx+120h], xmm7
    movdqu [rcx+130h], xmm8
    movdqu [rcx+140h], xmm9
    movdqu [rcx+150h], xmm10
    movdqu [rcx+160h], xmm11
    movdqu [rcx+170h], xmm12
    movdqu [rcx+180h], xmm13
    movdqu [rcx+190h], xmm14
    movdqu [rcx+1a0h], xmm15
    stmxcsr dword ptr [rcx+1b0h]
    fnstcw word ptr [rcx+1b4h]
    mov r11, [rsp]
    mov [rcx+18h], r11
    jmp body
ENDM
.code
; Own fixture leaf. Snapshot the first loop state after replay, before any
; C++ argument preparation. Normalize only RSP to the source CALL-before value.
; MOV/LEA/RET and restoring R11 do not modify captured registers or flags.
RtLfLoopSnapshot PROC
    mov [rdi+1c0h], rax
    mov [rdi+1c8h], rcx
    mov [rdi+1d0h], rdx
    mov [rdi+1d8h], rbx
    mov [rdi+1e8h], rbp
    mov [rdi+1f0h], rsi
    mov [rdi+1f8h], rdi
    mov [rdi+200h], r8
    mov [rdi+208h], r9
    mov [rdi+210h], r10
    mov [rdi+218h], r11
    mov [rdi+220h], r12
    mov [rdi+228h], r13
    mov [rdi+230h], r14
    mov [rdi+238h], r15
    lea r11, [rsp+8]
    mov [rdi+1e0h], r11
    movdqu [rdi+240h], xmm0
    movdqu [rdi+250h], xmm1
    movdqu [rdi+260h], xmm2
    movdqu [rdi+270h], xmm3
    movdqu [rdi+280h], xmm4
    movdqu [rdi+290h], xmm5
    movdqu [rdi+2a0h], xmm6
    movdqu [rdi+2b0h], xmm7
    movdqu [rdi+2c0h], xmm8
    movdqu [rdi+2d0h], xmm9
    movdqu [rdi+2e0h], xmm10
    movdqu [rdi+2f0h], xmm11
    movdqu [rdi+300h], xmm12
    movdqu [rdi+310h], xmm13
    movdqu [rdi+320h], xmm14
    movdqu [rdi+330h], xmm15
    stmxcsr dword ptr [rdi+340h]
    fnstcw word ptr [rdi+344h]
    mov r11, [rdi+218h]
    ret
RtLfLoopSnapshot ENDP
PUBLIC RtLfDirect, RtLfVirtual
RtLfDirect PROC
    CALLEE_SNAPSHOT RtLfDirectBody
RtLfDirect ENDP
RtLfVirtual PROC
    CALLEE_SNAPSHOT RtLfVirtualBody
RtLfVirtual ENDP
LISTENER_PARENT RtLfTarget0, RtLfTarget0End, RtLfLoop0, \
    RtLfMid0, RtLfMidReturn0, RtLfSite00, \
    RtLfSite01, RtLfSite02, RtLfPost00, \
    RtLfPost01, RtLfPost02
LISTENER_PARENT RtLfTarget1, RtLfTarget1End, RtLfLoop1, \
    RtLfMid1, RtLfMidReturn1, RtLfSite10, \
    RtLfSite11, RtLfSite12, RtLfPost10, \
    RtLfPost11, RtLfPost12

PUBLIC RtLfCall, RtLfCallerReturn
RtLfCall PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    mov rdx, r8
    call rax
RtLfCallerReturn LABEL BYTE
    add rsp, 28h
    ret
RtLfCall ENDP

PUBLIC RtLfClobber
RtLfClobber PROC
    mov rax, 0deadbeef12345678h
    mov rcx, 0deadbeef23456789h
    mov rdx, 0deadbeef3456789ah
    mov r8, 0deadbeef456789abh
    mov r9, 0deadbeef56789abch
    mov r10, 0deadbeef6789abcdh
    mov r11, 0deadbeef789abcdeh
    pcmpeqd xmm0, xmm0
    pxor xmm1, xmm1
    pcmpeqd xmm2, xmm2
    pxor xmm3, xmm3
    pcmpeqd xmm4, xmm4
    pxor xmm5, xmm5
    clc
    ret
RtLfClobber ENDP
END
