; Self-owned source functions, not a production patch installation proof.
OPTION CASEMAP:NONE
EXTERN RtCancellationInlineEntry:QWORD
EXTERN RtCancellationStopAndDrop:PROC
PUBLIC RtCancellationInlineBeginSite,RtCancellationInlineBeginReturn,RtCancellationInlineFreeSite,RtCancellationInlineFreeReturn
PUBLIC RtCancellationInlineRetainedSite,RtCancellationInlineRetainedReturn
EXTERN RtWaitFixture_YieldPrepare:PROC
EXTERN RtWaitFixture_Owner:PROC
EXTERN RtWaitFixture_StartPrepare:PROC
EXTERN RtWaitFixture_StartFinish:PROC
EXTERN RtWaitFixture_ReleasePrepare:PROC
EXTERN RtWaitFixture_ReleaseFinish:PROC
EXTERN RtWaitFixture_Nested:PROC
EXTERN RtWaitFixture_SharedReturn:PROC
EXTERN RtWaitFixture_CompleteOriginal:PROC
EXTERN RtWaitFixture_CallbackEntry:QWORD
EXTERN RtWaitFixture_ReleaseEntry:QWORD
EXTERN RtWaitFixture_RipValue:QWORD
EXTERN RtWaitFixture_Before:BYTE
EXTERN RtWaitFixture_After:BYTE
CAPTURE MACRO buffer:req, phase:req
    mov qword ptr [buffer+phase*1b0h+020h], rax
    mov qword ptr [buffer+phase*1b0h+028h], rcx
    mov qword ptr [buffer+phase*1b0h+030h], rdx
    mov qword ptr [buffer+phase*1b0h+038h], rbx
    mov qword ptr [buffer+phase*1b0h+040h], rsp
    mov qword ptr [buffer+phase*1b0h+048h], rbp
    mov qword ptr [buffer+phase*1b0h+050h], rsi
    mov qword ptr [buffer+phase*1b0h+058h], rdi
    mov qword ptr [buffer+phase*1b0h+060h], r8
    mov qword ptr [buffer+phase*1b0h+068h], r9
    mov qword ptr [buffer+phase*1b0h+070h], r10
    mov qword ptr [buffer+phase*1b0h+078h], r11
    mov qword ptr [buffer+phase*1b0h+080h], r12
    mov qword ptr [buffer+phase*1b0h+088h], r13
    mov qword ptr [buffer+phase*1b0h+090h], r14
    mov qword ptr [buffer+phase*1b0h+098h], r15
    pushfq
    pop qword ptr [buffer+phase*1b0h+018h]
    movdqu xmmword ptr [buffer+phase*1b0h+0a0h], xmm0
    movdqu xmmword ptr [buffer+phase*1b0h+0b0h], xmm1
    movdqu xmmword ptr [buffer+phase*1b0h+0c0h], xmm2
    movdqu xmmword ptr [buffer+phase*1b0h+0d0h], xmm3
    movdqu xmmword ptr [buffer+phase*1b0h+0e0h], xmm4
    movdqu xmmword ptr [buffer+phase*1b0h+0f0h], xmm5
    movdqu xmmword ptr [buffer+phase*1b0h+0100h], xmm6
    movdqu xmmword ptr [buffer+phase*1b0h+0110h], xmm7
    movdqu xmmword ptr [buffer+phase*1b0h+0120h], xmm8
    movdqu xmmword ptr [buffer+phase*1b0h+0130h], xmm9
    movdqu xmmword ptr [buffer+phase*1b0h+0140h], xmm10
    movdqu xmmword ptr [buffer+phase*1b0h+0150h], xmm11
    movdqu xmmword ptr [buffer+phase*1b0h+0160h], xmm12
    movdqu xmmword ptr [buffer+phase*1b0h+0170h], xmm13
    movdqu xmmword ptr [buffer+phase*1b0h+0180h], xmm14
    movdqu xmmword ptr [buffer+phase*1b0h+0190h], xmm15
    stmxcsr dword ptr [buffer+phase*1b0h+1a0h]
    fnstcw word ptr [buffer+phase*1b0h+1a4h]
ENDM
PROLOG MACRO
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
    sub rsp, 68h
    .allocstack 68h
    .endprolog
ENDM
EPILOG MACRO
    add rsp, 68h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
ENDM
.code

PUBLIC RtWaitFixture_CallbackTarget, RtWaitFixture_CleanupTarget
RtWaitFixture_CallbackTarget PROC
    jmp qword ptr [RtWaitFixture_CallbackEntry]
RtWaitFixture_CallbackTarget ENDP
RtWaitFixture_CleanupTarget PROC
    jmp qword ptr [RtWaitFixture_ReleaseEntry]
RtWaitFixture_CleanupTarget ENDP
PUBLIC RtWaitFixture_Yield, RtWaitFixture_YieldEnd
PUBLIC RtWaitFixture_Phase0, RtWaitFixture_After0
PUBLIC RtWaitFixture_Phase1, RtWaitFixture_After1
PUBLIC RtWaitFixture_Phase2, RtWaitFixture_After2
PUBLIC RtWaitFixture_Phase3, RtWaitFixture_After3
PUBLIC RtWaitFixture_YieldOldReturn, RtWaitFixture_YieldDone
RtWaitFixture_Yield PROC FRAME
    PROLOG
    mov r14, rcx
    mov rdi, rdx
    mov ebx, 12345678h
    call RtWaitFixture_YieldPrepare
    test eax, eax
    jz RtWaitFixture_YieldDone
    CAPTURE RtWaitFixture_Before, 0
RtWaitFixture_Phase0 LABEL BYTE
    mov [rdi+28h], ebx
    lea rax, RtWaitFixture_CallbackTarget
RtWaitFixture_After0 LABEL BYTE
    CAPTURE RtWaitFixture_After, 0
    mov [rdi+10h], rax
    lea rax, RtWaitFixture_CleanupTarget
    mov [rdi+18h], rax
    mov [rdi+20h], r14
    mov rcx, [r14+78h]
    test rcx, rcx
RtWaitFixture_Phase1 LABEL BYTE
    jz short RtWaitFixture_Phase2
    add rcx, 8
RtWaitFixture_After1 LABEL BYTE
    call RtWaitFixture_SharedReturn
RtWaitFixture_YieldOldReturn LABEL BYTE
RtWaitFixture_Phase2 LABEL NEAR
    mov [r14+78h], rdi
    lock inc dword ptr [rdi+0ch]
RtWaitFixture_After2 LABEL BYTE
RtWaitFixture_Phase3 LABEL BYTE
    db 0e9h
    dd RtWaitFixture_YieldDone-($+4)
RtWaitFixture_After3 LABEL BYTE
    int 3
RtWaitFixture_YieldDone LABEL NEAR
    mov ecx, 0
    call RtWaitFixture_CompleteOriginal
    EPILOG
RtWaitFixture_YieldEnd LABEL BYTE
RtWaitFixture_Yield ENDP
PUBLIC RtWaitFixture_Dispatch, RtWaitFixture_DispatchEnd
PUBLIC RtWaitFixture_Phase12, RtWaitFixture_After12
PUBLIC RtWaitFixture_Phase13, RtWaitFixture_After13
PUBLIC RtWaitFixture_Phase14, RtWaitFixture_After14
PUBLIC RtWaitFixture_Phase15, RtWaitFixture_After15
PUBLIC RtWaitFixture_DispatchCallbackReturn, RtWaitFixture_DispatchCleanupReturn
PUBLIC RtWaitFixture_DispatchCleanupLoad
RtWaitFixture_Dispatch PROC FRAME
    PROLOG
    mov rdi, rcx
    cmp qword ptr [rdi+10h], 0
    je DispatchDone
    lea rcx, [rdi+28h]
    call RtWaitFixture_Owner
RtWaitFixture_Phase12 LABEL BYTE
    test rax, rax
    jz short RtWaitFixture_DispatchCleanupLoad
RtWaitFixture_After12 LABEL BYTE
RtWaitFixture_Phase13 LABEL BYTE
    mov rdx, [rdi+20h]
    mov rcx, rax
RtWaitFixture_After13 LABEL BYTE
    call qword ptr [rdi+10h]
RtWaitFixture_DispatchCallbackReturn LABEL BYTE
RtWaitFixture_DispatchCleanupLoad LABEL NEAR
    mov rax, [rdi+18h]
RtWaitFixture_Phase14 LABEL BYTE
    test rax, rax
    jz short RtWaitFixture_DispatchCleanupReturn
RtWaitFixture_After14 LABEL BYTE
    mov rcx, [rdi+20h]
    call rax
RtWaitFixture_DispatchCleanupReturn LABEL NEAR
    CAPTURE RtWaitFixture_Before, 15
RtWaitFixture_Phase15 LABEL BYTE
    mov qword ptr [rdi+10h], 0
RtWaitFixture_After15 LABEL BYTE
    CAPTURE RtWaitFixture_After, 15
DispatchDone:
    mov ecx, 1
    call RtWaitFixture_CompleteOriginal
    EPILOG
RtWaitFixture_DispatchEnd LABEL BYTE
RtWaitFixture_Dispatch ENDP
PUBLIC RtWaitFixture_Batch, RtWaitFixture_BatchEnd
PUBLIC RtWaitFixture_Phase16, RtWaitFixture_After16
PUBLIC RtWaitFixture_Phase17, RtWaitFixture_After17
PUBLIC RtWaitFixture_BatchSkipCleanup, RtWaitFixture_BatchCleanupReturn, RtWaitFixture_BatchSharedReturn
RtWaitFixture_Batch PROC FRAME
    PROLOG
    mov rsi, rcx
    xor edi, edi
    xor r14d, r14d
    cmp qword ptr [rsi+1a0h], 0
    je BatchDone
BatchLoop:
    mov rax, [rsi+190h]
    mov rbp, [rax+rdi*8]
RtWaitFixture_Phase16 LABEL BYTE
    cmp [rbp+10h], r14
    jz short RtWaitFixture_BatchSkipCleanup
RtWaitFixture_After16 LABEL BYTE
    mov rcx, [rbp+20h]
    call qword ptr [rbp+18h]
RtWaitFixture_BatchCleanupReturn LABEL BYTE
    mov [rbp+10h], r14
RtWaitFixture_BatchSkipCleanup LABEL NEAR
    mov rax, [rsi+190h]
    mov rcx, [rax+rdi*8]
    add rcx, 8
    call RtWaitFixture_SharedReturn
RtWaitFixture_BatchSharedReturn LABEL BYTE
    CAPTURE RtWaitFixture_Before, 17
RtWaitFixture_Phase17 LABEL BYTE
    inc rdi
    cmp rdi, [rsi+1a0h]
RtWaitFixture_After17 LABEL BYTE
    CAPTURE RtWaitFixture_After, 17
    jb BatchLoop
BatchDone:
    mov ecx, 2
    call RtWaitFixture_CompleteOriginal
    EPILOG
RtWaitFixture_BatchEnd LABEL BYTE
RtWaitFixture_Batch ENDP
PUBLIC RtWaitFixture_Start, RtWaitFixture_StartEnd
PUBLIC RtWaitFixture_Phase9, RtWaitFixture_After9
PUBLIC RtWaitFixture_Phase10, RtWaitFixture_After10
PUBLIC RtWaitFixture_Phase11, RtWaitFixture_After11, RtWaitFixture_StartSharedReturn
RtWaitFixture_Start PROC FRAME
    PROLOG
    mov r12, r9
    mov [rsp+40h], rcx
    mov [rsp+48h], rdx
    call RtWaitFixture_StartPrepare
    mov rbx, rax
RtCancellationInlineBeginSite LABEL BYTE
    call qword ptr [RtCancellationInlineEntry]
RtCancellationInlineBeginReturn LABEL BYTE
    mov rcx, rbx
    call RtCancellationStopAndDrop
    xor edi, edi
    mov rax, [rbx+78h]
    test rax, rax
    jz StartFinish
    CAPTURE RtWaitFixture_Before, 9
RtWaitFixture_Phase9 LABEL BYTE
    mov [rax+28h], edi
    mov [rax+10h], rdi
RtWaitFixture_After9 LABEL BYTE
    CAPTURE RtWaitFixture_After, 9
    mov [rax+18h], rdi
    mov [rax+20h], rdi
RtWaitFixture_Phase10 LABEL BYTE
    mov rcx, [rbx+78h]
    add rcx, 8
RtWaitFixture_After10 LABEL BYTE
    call RtWaitFixture_SharedReturn
RtWaitFixture_StartSharedReturn LABEL BYTE
    CAPTURE RtWaitFixture_Before, 11
RtWaitFixture_Phase11 LABEL BYTE
    mov [rbx+78h], rdi
    cmp qword ptr [rbx+10h], 0
RtWaitFixture_After11 LABEL BYTE
    CAPTURE RtWaitFixture_After, 11
StartFinish:
    xor eax, eax
RtCancellationInlineFreeSite LABEL BYTE
    call qword ptr [RtCancellationInlineEntry]
RtCancellationInlineFreeReturn LABEL BYTE
    mov rcx, rbx
    mov rdx, r12
    call RtWaitFixture_StartFinish
    EPILOG
RtWaitFixture_StartEnd LABEL BYTE
RtWaitFixture_Start ENDP
RtCancellationRetainedSite PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
RtCancellationInlineRetainedSite LABEL BYTE
    call qword ptr [RtCancellationInlineEntry]
RtCancellationInlineRetainedReturn LABEL BYTE
    add rsp, 28h
    ret
RtCancellationRetainedSite ENDP

PUBLIC RtWaitFixture_Release, RtWaitFixture_ReleaseEnd
PUBLIC RtWaitFixture_Phase6, RtWaitFixture_After6
PUBLIC RtWaitFixture_Phase7, RtWaitFixture_After7
PUBLIC RtWaitFixture_Phase8, RtWaitFixture_After8, RtWaitFixture_ReleaseSharedReturn
RtWaitFixture_Release PROC FRAME
    PROLOG
    mov rbx, rcx
    xor edi, edi
    call RtWaitFixture_ReleasePrepare
    test eax, eax
    jz ReleaseFinish
    mov rax, [rbx+78h]
    test rax, rax
    jz ReleaseFinish
    CAPTURE RtWaitFixture_Before, 6
RtWaitFixture_Phase6 LABEL BYTE
    mov [rax+28h], edi
    mov [rax+10h], rdi
RtWaitFixture_After6 LABEL BYTE
    CAPTURE RtWaitFixture_After, 6
    mov [rax+18h], rdi
    mov [rax+20h], rdi
RtWaitFixture_Phase7 LABEL BYTE
    mov rcx, [rbx+78h]
    add rcx, 8
RtWaitFixture_After7 LABEL BYTE
    call RtWaitFixture_SharedReturn
RtWaitFixture_ReleaseSharedReturn LABEL BYTE
    CAPTURE RtWaitFixture_Before, 8
RtWaitFixture_Phase8 LABEL BYTE
    mov [rbx+78h], rdi
    cmp [rbx+10h], rdi
RtWaitFixture_After8 LABEL BYTE
    CAPTURE RtWaitFixture_After, 8
ReleaseFinish:
    mov rcx, rbx
    call RtWaitFixture_ReleaseFinish
    EPILOG
RtWaitFixture_ReleaseEnd LABEL BYTE
RtWaitFixture_Release ENDP
PUBLIC RtWaitFixture_Detach, RtWaitFixture_DetachEnd
PUBLIC RtWaitFixture_Phase4, RtWaitFixture_After4
PUBLIC RtWaitFixture_Phase5, RtWaitFixture_After5
RtWaitFixture_Detach PROC FRAME
    PROLOG
    mov r12, rcx
    mov rdi, rcx
    xor ebp, ebp
    mov rax, [rdi+78h]
    test rax, rax
    jz DetachNested
    CAPTURE RtWaitFixture_Before, 4
RtWaitFixture_Phase4 LABEL BYTE
    mov [rax+28h], ebp
    mov rcx, [RtWaitFixture_RipValue]
RtWaitFixture_After4 LABEL BYTE
    CAPTURE RtWaitFixture_After, 4
DetachNested:
    mov rcx, r12
    call RtWaitFixture_Nested
    test rax, rax
    jz DetachDone
    mov rdi, rax
    mov rax, [rdi+78h]
    test rax, rax
    jz DetachDone
    CAPTURE RtWaitFixture_Before, 5
RtWaitFixture_Phase5 LABEL BYTE
    mov [rax+28h], ebp
    mov rcx, [RtWaitFixture_RipValue]
RtWaitFixture_After5 LABEL BYTE
    CAPTURE RtWaitFixture_After, 5
DetachDone:
    mov ecx, 3
    call RtWaitFixture_CompleteOriginal
    EPILOG
RtWaitFixture_DetachEnd LABEL BYTE
RtWaitFixture_Detach ENDP
PUBLIC RtWaitFixture_TestFlags, RtWaitFixture_CmpFlags, RtWaitFixture_IncFlags
RtWaitFixture_TestFlags PROC
    push rdx
    popfq
    test rcx, rcx
    pushfq
    pop rax
    ret
RtWaitFixture_TestFlags ENDP
RtWaitFixture_CmpFlags PROC
    push r8
    popfq
    cmp rcx, rdx
    pushfq
    pop rax
    ret
RtWaitFixture_CmpFlags ENDP
RtWaitFixture_IncFlags PROC
    push rdx
    popfq
    inc dword ptr [rcx]
    pushfq
    pop rax
    ret
RtWaitFixture_IncFlags ENDP
END
