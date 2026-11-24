option casemap:none
EXTERN FixtureFirstSetClipEntry:QWORD
PUBLIC FixtureFirstSetClipTarget, FixtureFirstStartOriginal, FixtureFirstClipReturned
.code
ALIGN 16
; The real parent original makes an E8 call. This tail jump keeps that exact
; caller address when the source helper records its SetClip child.
FixtureFirstSetClipTarget PROC
    jmp qword ptr [FixtureFirstSetClipEntry]
FixtureFirstSetClipTarget ENDP
ALIGN 16
FixtureFirstStartOriginal PROC FRAME
    push rbx
    .pushreg rbx
    sub rsp,20h
    .allocstack 20h
    .endprolog
    mov rbx,rcx
    cmp dword ptr [rbx+10h],0
    jne final_call
    mov dword ptr [rbx+10h],1
    mov eax,1
    jmp short start_done
final_call:
    mov dword ptr [rbx+10h],-1
    mov rcx,qword ptr [rbx+20h]
    mov rcx,qword ptr [rcx+28h]
    mov rdx,qword ptr [rbx+38h]
    xor r8d,r8d
    call FixtureFirstSetClipTarget
FixtureFirstClipReturned LABEL BYTE
    mov rax,qword ptr [rbx+40h]
    mov rcx,qword ptr [rbx+20h]
    mov rdx,qword ptr [rcx+28h]
    mov qword ptr [rax+10h],rdx
    mov qword ptr [rcx+38h],rax
    xor eax,eax
start_done:
    add rsp,20h
    pop rbx
    ret
FixtureFirstStartOriginal ENDP
END
