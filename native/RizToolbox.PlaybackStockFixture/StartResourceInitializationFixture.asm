option casemap:none
option dotname
EXTERN StockSetClip:PROC
EXTERN StockStartBoundaryBody:PROC
EXTERN StockStartAllocatePlayer:PROC
EXTERN StockStartMissingResource:PROC
EXTERN StockPlayerConstructor:PROC
PUBLIC StockStartMoveNext, StockStartMoveNextEnd, StockStartClipReturned
PUBLIC StockStartResourceLoaded, StockStartResourceCall, StockStartResourceReturned
.code
ALIGN 16
StockStartMoveNext PROC
    mov qword ptr [rsp+18h],rbp
    push rsi
    sub rsp,20h
    mov rsi,rcx
    inc dword ptr [rsi+30h]
    mov rcx,qword ptr [rsi+48h]
    test rcx,rcx
    jz start_after_waituntil
    mov rdx,qword ptr [rsi+50h]
    mov r8,qword ptr [rsi+58h]
    call StockPlayerConstructor
start_after_waituntil:
    cmp dword ptr [rsi+10h],0
    je start_yield
    cmp dword ptr [rsi+10h],1
    jne start_false
    mov dword ptr [rsi+10h],-1
    mov rbp,qword ptr [rsi+20h]
    mov rcx,rsi
    mov edx,1
    call StockStartBoundaryBody
    ; A real consumer: reload the current reference from Progress, dispatch
    ; that object's getter, and pass its actual result to the hooked child.
    ; The enumerator's compatibility Clip cache at +38 is never read here.
    mov rcx,qword ptr [rbp+30h]
StockStartResourceLoaded LABEL BYTE
    test rcx,rcx
    jnz start_has_resource
    call StockStartMissingResource
start_has_resource:
    mov rax,qword ptr [rcx+10h]
    xor edx,edx
StockStartResourceCall LABEL BYTE
    call rax
StockStartResourceReturned LABEL BYTE
    mov rcx,qword ptr [rbp+28h]
    mov rdx,rax
    xor r8d,r8d
    call StockSetClip
StockStartClipReturned LABEL BYTE
    mov rcx,rsi
    mov edx,2
    call StockStartBoundaryBody
    mov rcx,rsi
    call StockStartAllocatePlayer
    mov qword ptr [rsi+40h],rax
    mov rdx,qword ptr [rbp+28h]
    mov qword ptr [rax+10h],rdx
    mov rcx,rsi
    mov edx,3
    call StockStartBoundaryBody
    mov rax,qword ptr [rsi+40h]
    mov qword ptr [rbp+38h],rax
    mov rcx,rsi
    mov edx,4
    call StockStartBoundaryBody
start_false:
    xor eax,eax
    jmp short start_return
start_yield:
    mov dword ptr [rsi+10h],1
    mov eax,1
start_return:
    test dword ptr [rsi+28h],1
    jz start_epilog
    mov eax,dword ptr [rsi+2ch]
start_epilog:
    mov rbp,qword ptr [rsp+40h]
    add rsp,20h
    pop rsi
    ret
    DB 464-($-StockStartMoveNext) DUP(090h)
StockStartMoveNext ENDP
StockStartMoveNextEnd LABEL BYTE
.xdata SEGMENT READONLY
ALIGN 4
StockStartUnwind LABEL BYTE
    DB 1,10,4,0,10,054h,8,0,10,032h,6,060h
.xdata ENDS
.pdata SEGMENT READONLY
ALIGN 4
    DD IMAGEREL StockStartMoveNext, IMAGEREL StockStartMoveNextEnd, IMAGEREL StockStartUnwind
.pdata ENDS
END
