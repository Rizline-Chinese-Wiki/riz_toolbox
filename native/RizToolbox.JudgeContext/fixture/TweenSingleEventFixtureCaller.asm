; Owned Single body with the actual register roles and three CALL widths.
; Every pointer/function belongs to this EXE. No game image is loaded.
OPTION CASEMAP:NONE
OPTION DOTNAME
EXTERN RizTweenSingleEventContext_MidEntry:PROC
EXTERN RizTweenSingleEventContext_PreEntry:PROC
EXTERN RizTweenSingleEventContext_PostEntry:PROC
EXTERN RtSingleFixture_OriginalBegin:PROC
EXTERN RtSingleFixture_BeforeCalls:PROC
EXTERN RtSingleFixture_Box:PROC
EXTERN RtSingleFixture_CppTargets:QWORD
EXTERN RtSingleFixture_MidBeforeState:BYTE
EXTERN RtSingleFixture_MidAfterState:BYTE
EXTERN RtSingleFixture_TargetState:BYTE
EXTERN RtSingleFixture_LoopState:BYTE
EXTERN RtSingleFixture_MidPolicy:DWORD
PUBLIC RtSingleFixture_Original
PUBLIC RtSingleFixture_PrimaryEnd
PUBLIC RtSingleFixture_Continuation
PUBLIC RtSingleFixture_ContinuationEnd
PUBLIC RtSingleFixture_MidReturn
PUBLIC RtSingleFixture_Return0
PUBLIC RtSingleFixture_Return1
PUBLIC RtSingleFixture_Return2
PUBLIC RtSingleFixture_Loop
PUBLIC RtSingleFixture_TypedTarget
PUBLIC RtSingleFixture_VoidTarget
PUBLIC RtSingleFixture_ArrayTarget

; Direct stores preserve all GPRs, SIMD lanes, flags and FP control state.
; These owned-only snapshots never participate in producer authorization.
CAPTURE_SINGLE_STATE MACRO destination
    mov qword ptr [destination+020h],rax
    mov qword ptr [destination+028h],rcx
    mov qword ptr [destination+030h],rdx
    mov qword ptr [destination+038h],rbx
    mov qword ptr [destination+040h],rsp
    mov qword ptr [destination+048h],rbp
    mov qword ptr [destination+050h],rsi
    mov qword ptr [destination+058h],rdi
    mov qword ptr [destination+060h],r8
    mov qword ptr [destination+068h],r9
    mov qword ptr [destination+070h],r10
    mov qword ptr [destination+078h],r11
    mov qword ptr [destination+080h],r12
    mov qword ptr [destination+088h],r13
    mov qword ptr [destination+090h],r14
    mov qword ptr [destination+098h],r15
    pushfq
    pop qword ptr [destination+018h]
    movdqu xmmword ptr [destination+0a0h],xmm0
    movdqu xmmword ptr [destination+0b0h],xmm1
    movdqu xmmword ptr [destination+0c0h],xmm2
    movdqu xmmword ptr [destination+0d0h],xmm3
    movdqu xmmword ptr [destination+0e0h],xmm4
    movdqu xmmword ptr [destination+0f0h],xmm5
    movdqu xmmword ptr [destination+100h],xmm6
    movdqu xmmword ptr [destination+110h],xmm7
    movdqu xmmword ptr [destination+120h],xmm8
    movdqu xmmword ptr [destination+130h],xmm9
    movdqu xmmword ptr [destination+140h],xmm10
    movdqu xmmword ptr [destination+150h],xmm11
    movdqu xmmword ptr [destination+160h],xmm12
    movdqu xmmword ptr [destination+170h],xmm13
    movdqu xmmword ptr [destination+180h],xmm14
    movdqu xmmword ptr [destination+190h],xmm15
    stmxcsr dword ptr [destination+1a0h]
    fnstcw word ptr [destination+1a4h]
ENDM

.code
RtSingleFixture_Original PROC
    push rbp
    push r13
    sub rsp,048h
    movaps [rsp+020h],xmm6
    mov [rsp+030h],r15
    mov rbp,rcx
    mov r13,r8
    movaps xmm6,xmm1
    call RtSingleFixture_OriginalBegin
RtSingleFixture_PrimaryEnd LABEL BYTE
RtSingleFixture_Original ENDP

; The controlled continuation is registered separately with its five SAVE_NONVOL
; codes and CHAININFO to the actual primary RF. Dynamic post stubs need all five.
RtSingleFixture_Continuation LABEL BYTE
    mov [rsp+070h],rsi
    mov [rsp+040h],r12
    mov [rsp+068h],rbx
    mov [rsp+078h],rdi
    mov [rsp+038h],r14
    mov r15,[rbp+010h]
    xor esi,esi
    xor ecx,ecx
    cmp RtSingleFixture_MidPolicy,1
    je RtSingleFixture_AfterMid
RtSingleFixture_MidCall:
    CAPTURE_SINGLE_STATE RtSingleFixture_MidBeforeState
    call RizTweenSingleEventContext_MidEntry
RtSingleFixture_MidReturn LABEL BYTE
    db 090h,090h,090h,090h
    CAPTURE_SINGLE_STATE RtSingleFixture_MidAfterState
RtSingleFixture_AfterMid:
    cmp RtSingleFixture_MidPolicy,2
    jne RtSingleFixture_BeforeListeners
    mov RtSingleFixture_MidPolicy,0
    xor esi,esi
    xor ecx,ecx
    jmp RtSingleFixture_MidCall
RtSingleFixture_BeforeListeners:
    mov rcx,rbp
    call RtSingleFixture_BeforeCalls
    mov ecx,esi
RtSingleFixture_Loop::
    CAPTURE_SINGLE_STATE RtSingleFixture_LoopState
    cmp ecx,[r15+018h]
    jge RtSingleFixture_Done
    mov rax,[r15+010h]
    mov rbx,[rax+rsi*8]
    mov eax,[rbx+8]
    cmp eax,1
    je RtSingleFixture_Void
    cmp eax,2
    je RtSingleFixture_Array
    mov rax,[rbx]
    mov r8,[rax+01a0h]
    movaps xmm1,xmm6
    mov rcx,rbx
    db 090h
    call RizTweenSingleEventContext_PreEntry
RtSingleFixture_Return0 LABEL BYTE
    ; Setup publishes the real 5B relative post JMP plus four NOPs here.
    ; FF25 would look like a completed epilog to the Windows unwinder.
    db 9 dup (090h)
RtSingleFixture_Void:
    xor edx,edx
    mov rcx,rbx
    call RizTweenSingleEventContext_PreEntry
RtSingleFixture_Return1 LABEL BYTE
    db 9 dup (090h)
RtSingleFixture_Array:
    mov rcx,rbp
    movd edx,xmm6
    call RtSingleFixture_Box
    mov rax,[rbx]
    mov r14,rbx
    mov rcx,r14
    mov rdx,[rbp+028h]
    mov r8,[rax+0180h]
    db 090h
    call RizTweenSingleEventContext_PreEntry
RtSingleFixture_Return2 LABEL BYTE
    db 9 dup (090h)
RtSingleFixture_Done:
    mov r14,[rsp+038h]
    mov rdi,[rsp+078h]
    mov rbx,[rsp+068h]
    mov r12,[rsp+040h]
    mov rsi,[rsp+070h]
    mov r15,[rsp+030h]
    movaps xmm6,[rsp+020h]
    add rsp,048h
    pop r13
    pop rbp
    ret
RtSingleFixture_ContinuationEnd LABEL BYTE

; Leaf target adapters preserve the physical source return slot and expose
; actual target-entry machine state before the C++ listeners use any register.
RtSingleFixture_TypedTarget PROC
    CAPTURE_SINGLE_STATE RtSingleFixture_TargetState
    jmp qword ptr [RtSingleFixture_CppTargets]
RtSingleFixture_TypedTarget ENDP
RtSingleFixture_VoidTarget PROC
    CAPTURE_SINGLE_STATE RtSingleFixture_TargetState
    jmp qword ptr [RtSingleFixture_CppTargets+8]
RtSingleFixture_VoidTarget ENDP
RtSingleFixture_ArrayTarget PROC
    CAPTURE_SINGLE_STATE RtSingleFixture_TargetState
    jmp qword ptr [RtSingleFixture_CppTargets+16]
RtSingleFixture_ArrayTarget ENDP

; Explicit owned .pdata makes both physical ranges discoverable by the OS.
; Primary bytes equal MASM's FRAME output for the same concrete instructions.
.xdata SEGMENT READONLY
ALIGN 4
RtSingleFixture_PrimaryUnwind LABEL BYTE
    DB 01h,011h,7,0,011h,0f4h,6,0,00ch,068h,2,0,7,082h,3,0d0h,1,050h,0,0
RtSingleFixture_ContinuationUnwind LABEL BYTE
    DB 021h,25,10,0,25,0e4h,7,0,20,074h,15,0,15,034h,13,0,10,0c4h,8,0,5,064h,14,0
    DD IMAGEREL RtSingleFixture_Original
    DD IMAGEREL RtSingleFixture_PrimaryEnd
    DD IMAGEREL RtSingleFixture_PrimaryUnwind
.xdata ENDS
.pdata SEGMENT READONLY
    DD IMAGEREL RtSingleFixture_Original
    DD IMAGEREL RtSingleFixture_PrimaryEnd
    DD IMAGEREL RtSingleFixture_PrimaryUnwind
    DD IMAGEREL RtSingleFixture_Continuation
    DD IMAGEREL RtSingleFixture_ContinuationEnd
    DD IMAGEREL RtSingleFixture_ContinuationUnwind
.pdata ENDS
END
