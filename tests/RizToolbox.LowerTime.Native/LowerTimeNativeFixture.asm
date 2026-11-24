option casemap:none
EXTERN WrapperEntry:QWORD, LowerEntry:QWORD, ClipEntry:QWORD, StartEntry:QWORD
EXTERN PauseEntry:QWORD, PauseLeafEntry:QWORD, PitchEntry:QWORD
PUBLIC FixtureWrapperTarget, FixtureWrapperOriginal, FixtureBadWrapperOriginal
PUBLIC FixtureNoChildOriginal, FixtureDuplicateOriginal, FixtureWrongBitsOriginal, FixtureWrongSelfOriginal
PUBLIC FixtureLowerTarget, FixtureClipTarget, FixtureStartTarget, FixtureStartOriginal, FixtureClipReturned
PUBLIC FixtureCtorTarget, FixturePauseTarget, FixturePauseOriginal, FixturePauseLeafTarget, FixturePitchTarget
.code
FixtureWrapperTarget PROC
    jmp qword ptr [WrapperEntry]
FixtureWrapperTarget ENDP
FixtureWrapperOriginal PROC
    jmp qword ptr [LowerEntry]
FixtureWrapperOriginal ENDP
FixtureBadWrapperOriginal PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    call FixtureLowerTarget
    add rsp,28h
    ret
FixtureBadWrapperOriginal ENDP
FixtureNoChildOriginal PROC
    ret
FixtureNoChildOriginal ENDP
FixtureDuplicateOriginal PROC FRAME
    push rbx
    .pushreg rbx
    sub rsp,30h
    .allocstack 30h
    .endprolog
    mov rbx,rcx
    movd dword ptr [rsp+20h],xmm1
    call FixtureLowerTarget
    mov rcx,rbx
    movd xmm1,dword ptr [rsp+20h]
    call FixtureLowerTarget
    add rsp,30h
    pop rbx
    ret
FixtureDuplicateOriginal ENDP
FixtureWrongBitsOriginal PROC
    mov eax,40000000h
    movd xmm1,eax
    jmp qword ptr [LowerEntry]
FixtureWrongBitsOriginal ENDP
FixtureWrongSelfOriginal PROC
    mov rcx,r8
    jmp qword ptr [LowerEntry]
FixtureWrongSelfOriginal ENDP
FixtureLowerTarget PROC
    jmp qword ptr [LowerEntry]
FixtureLowerTarget ENDP
FixtureClipTarget PROC
    jmp qword ptr [ClipEntry]
FixtureClipTarget ENDP
FixtureCtorTarget PROC
    ret
FixtureCtorTarget ENDP
FixtureStartTarget PROC
    jmp qword ptr [StartEntry]
FixtureStartTarget ENDP
FixtureStartOriginal PROC FRAME
    push rbx
    .pushreg rbx
    sub rsp,20h
    .allocstack 20h
    .endprolog
    mov rbx,rcx
    cmp dword ptr [rbx+10h],0
    jne start_final
    mov dword ptr [rbx+10h],1
    mov eax,1
    jmp short start_done
start_final:
    mov dword ptr [rbx+10h],-1
    mov rcx,qword ptr [rbx+20h]
    mov rcx,qword ptr [rcx+28h]
    mov rdx,qword ptr [rbx+38h]
    xor r8d,r8d
    call FixtureClipTarget
FixtureClipReturned LABEL BYTE
    xor eax,eax
start_done:
    add rsp,20h
    pop rbx
    ret
FixtureStartOriginal ENDP
FixturePauseTarget PROC
    jmp qword ptr [PauseEntry]
FixturePauseTarget ENDP
FixturePauseOriginal PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    mov rcx,qword ptr [rcx+10h]
    xor edx,edx
    call FixturePauseLeafTarget
    add rsp,28h
    ret
FixturePauseOriginal ENDP
FixturePauseLeafTarget PROC
    jmp qword ptr [PauseLeafEntry]
FixturePauseLeafTarget ENDP
FixturePitchTarget PROC
    jmp qword ptr [PitchEntry]
FixturePitchTarget ENDP
END
