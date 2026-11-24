; A bounded owned Single original. Production TLS/entry/ReadCurrent code is linked unchanged.
; Event/list/array/delegate storage and the invoked SEC_IMAGE code are all fixture owned.
OPTION CASEMAP:NONE
EXTERN RizTweenSingleEventContext_MidEntry:PROC
EXTERN RizTweenSingleEventContext_PreEntry:PROC
EXTERN RizTweenSingleEventContext_PostEntry:PROC
EXTERN RtSinglePlayFixture_SkipPost:DWORD
EXTERN RtSinglePlayFixture_Originals:QWORD
PUBLIC RtSinglePlayFixture_Original
PUBLIC RtSinglePlayFixture_MidReturn
PUBLIC RtSinglePlayFixture_Source0
PUBLIC RtSinglePlayFixture_Source1
PUBLIC RtSinglePlayFixture_Source2
PUBLIC RtSinglePlayFixture_Post0
PUBLIC RtSinglePlayFixture_Post1
PUBLIC RtSinglePlayFixture_Post2
.code
RtSinglePlayFixture_Original PROC FRAME
    push rbp
    .pushreg rbp
    push r13
    .pushreg r13
    push r15
    .pushreg r15
    push rsi
    .pushreg rsi
    push rbx
    .pushreg rbx
    sub rsp,040h
    .allocstack 040h
    movaps [rsp+020h],xmm6
    .savexmm128 xmm6,020h
    .endprolog
    inc RtSinglePlayFixture_Originals
    mov rbp,rcx
    mov r13,r8
    movaps xmm6,xmm1
    mov r15,[rbp+010h]
    xor esi,esi
    xor ecx,ecx
    call RizTweenSingleEventContext_MidEntry
RtSinglePlayFixture_MidReturn LABEL BYTE
RtSinglePlayFixture_Loop:
    cmp ecx,[r15+018h]
    jge RtSinglePlayFixture_Done
    mov rax,[r15+010h]
    mov rbx,[rax+rsi*8+020h]
    xor edx,edx
    mov rcx,rbx
    call RizTweenSingleEventContext_PreEntry
RtSinglePlayFixture_Source1 LABEL BYTE
    cmp RtSinglePlayFixture_SkipPost,0
    jne RtSinglePlayFixture_Done
    call RizTweenSingleEventContext_PostEntry
RtSinglePlayFixture_Post1 LABEL BYTE
    inc esi
    mov ecx,esi
    jmp RtSinglePlayFixture_Loop
RtSinglePlayFixture_Done:
    movaps xmm6,[rsp+020h]
    add rsp,040h
    pop rbx
    pop rsi
    pop r15
    pop r13
    pop rbp
    ret
RtSinglePlayFixture_Original ENDP
; Unused sites still have separate immutable identity PCs, and cannot issue a child.
RtSinglePlayFixture_Source0 LABEL BYTE
    int 3
RtSinglePlayFixture_Source2 LABEL BYTE
    int 3
RtSinglePlayFixture_Post0 LABEL BYTE
    int 3
RtSinglePlayFixture_Post2 LABEL BYTE
    int 3
END
