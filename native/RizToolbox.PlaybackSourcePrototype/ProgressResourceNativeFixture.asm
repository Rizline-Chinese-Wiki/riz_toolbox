option casemap:none
EXTERN ResourceBarrierOriginal:PROC
EXTERN ResourceResolveReference:PROC
EXTERN ResourceSetClipEntry:QWORD
PUBLIC ResourceClipLeaf, ResourceSetClipTarget, ResourceStartOriginal, ResourceClipReturned
.code
ALIGN 16
; Exact owned 13-byte +30 store, +8 E9 GC-barrier-shaped continuation.
ResourceClipLeaf PROC
    mov qword ptr [rcx+30h],rdx
    add rcx,30h
    jmp ResourceBarrierOriginal
ResourceClipLeaf ENDP
ALIGN 16
ResourceSetClipTarget PROC
    jmp qword ptr [ResourceSetClipEntry]
ResourceSetClipTarget ENDP
ALIGN 16
ResourceStartOriginal PROC FRAME
    push rbx
    .pushreg rbx
    sub rsp,20h
    .allocstack 20h
    .endprolog
    mov rbx,rcx
    cmp dword ptr [rbx+10h],0
    jne resource_final
    mov dword ptr [rbx+10h],1
    mov eax,1
    jmp short resource_done
resource_final:
    mov dword ptr [rbx+10h],-1
    mov rcx,qword ptr [rbx+20h]
    mov rcx,qword ptr [rcx+30h]
    call ResourceResolveReference
    mov rdx,rax
    mov rcx,qword ptr [rbx+20h]
    mov rcx,qword ptr [rcx+28h]
    xor r8d,r8d
    call ResourceSetClipTarget
ResourceClipReturned LABEL BYTE
    xor eax,eax
resource_done:
    add rsp,20h
    pop rbx
    ret
ResourceStartOriginal ENDP
END
