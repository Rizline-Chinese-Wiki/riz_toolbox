option casemap:none
EXTERN OwnedPlayback_Body:PROC
EXTERN OwnedPlayback_Initialized:BYTE
PUBLIC OwnedPlayback_GetTime
.code
ALIGN 16
OwnedPlayback_GetTime PROC FRAME
    mov qword ptr [rsp+8],rbx
    push rdi
    .pushreg rdi
    sub rsp,50h
    .allocstack 50h
    .savereg rbx,60h
    .endprolog
    ; Same shape as actual GetTime. A proved five-byte detour relocates
    ; only MOV/PUSH; this RIP-relative CMP remains in the unchanged source tail.
    cmp byte ptr [OwnedPlayback_Initialized],0
    mov rbx,rcx
    jne initialized
    mov r8d,0
    jmp invoke_body
initialized:
    mov r8d,1
invoke_body:
    call OwnedPlayback_Body
    mov rbx,qword ptr [rsp+60h]
    add rsp,50h
    pop rdi
    ret
    DB 640-($-OwnedPlayback_GetTime) DUP(090h)
OwnedPlayback_GetTime ENDP
END
