option casemap:none
option dotname
EXTERN StockSetClip:PROC
EXTERN StockStartBoundaryBody:PROC
EXTERN StockStartAllocatePlayer:PROC
EXTERN StockPlayerConstructor:PROC
PUBLIC StockStartMoveNext, StockStartMoveNextEnd, StockStartClipReturned
.code
ALIGN 16
StockStartMoveNext PROC
    mov qword ptr [rsp+18h],rbp
    push rsi
    sub rsp,20h
    mov rsi,rcx
    inc dword ptr [rsi+30h]
    ; State0/state1 both execute the actual shared body for a WaitUntil
    ; receiver. Its class and address are foreign to the rooted MUG watch.
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
    mov rcx,qword ptr [rbp+28h]
    mov rdx,qword ptr [rsi+38h]
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
