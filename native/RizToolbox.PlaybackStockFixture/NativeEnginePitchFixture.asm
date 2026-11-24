option casemap:none
option dotname
EXTERN StockEngineWitness:PROC
EXTERN StockEngineLastReturn:QWORD
PUBLIC StockEngineSetPitch,StockEnginePitchPart2,StockEnginePitchPart3,StockEngineSetPitchEnd
PUBLIC StockEngineResubmit,StockEngineResubmitEnd,StockEngineResubmitReturned
.code
ALIGN 16
StockEngineSetPitch PROC
    mov rax,rsp
    mov qword ptr [rax+18h],rbx
    mov qword ptr [rax+20h],rdi
    push rbp
    lea rbp,[rax-5fh]
    sub rsp,0a0h
    mov rdi,rcx
    movaps xmmword ptr [rax-18h],xmm6
    movaps xmm6,xmm1
    mov qword ptr [rsp+20h],r8
    mov r10,qword ptr [rax]
    mov qword ptr [StockEngineLastReturn],r10
    mov rcx,rdi
    mov edx,1
    movd r8d,xmm6
    mov r9,qword ptr [rsp+20h]
    call StockEngineWitness
    test eax,eax
    jz near ptr StockEnginePitchPart3
    DB 341-($-StockEngineSetPitch) DUP(090h)
StockEnginePitchPart2 LABEL BYTE
    mov qword ptr [rsp+0b0h],rsi
    mov rsi,0123456789abcdefh
    movss dword ptr [rdi+1f4h],xmm6
    mov rcx,rdi
    mov edx,2
    movd r8d,xmm6
    mov r9,qword ptr [rsp+20h]
    call StockEngineWitness
    DB 273-8-($-StockEnginePitchPart2) DUP(090h)
    mov rsi,qword ptr [rsp+0b0h]
StockEnginePitchPart3 LABEL BYTE
    lea r11,[rsp+0a0h]
    mov rbx,qword ptr [r11+20h]
    mov rdi,qword ptr [r11+28h]
    movaps xmm6,xmmword ptr [r11-10h]
    mov rsp,r11
    pop rbp
    ret
StockEngineSetPitch ENDP
StockEngineSetPitchEnd LABEL BYTE
ALIGN 16
StockEngineResubmit PROC FRAME
    sub rsp,28h
    .allocstack 28h
    .endprolog
    movss xmm1,dword ptr [rcx+1f4h]
    call StockEngineSetPitch
StockEngineResubmitReturned LABEL BYTE
    add rsp,28h
    ret
    DB 64-($-StockEngineResubmit) DUP(090h)
StockEngineResubmit ENDP
StockEngineResubmitEnd LABEL BYTE
.const
ALIGN 4
StockEngineRootUnwind LABEL BYTE
    DB 01h,01eh,09h,00h,01eh,068h,09h,00h
    DB 017h,074h,019h,00h,017h,034h,018h,00h
    DB 017h,01h,014h,00h,00ch,050h,00h,00h
ALIGN 4
StockEnginePart2Unwind LABEL BYTE
    DB 021h,08h,02h,00h,08h,064h,016h,00h
    DD imagerel StockEngineSetPitch,imagerel StockEnginePitchPart2,imagerel StockEngineRootUnwind
ALIGN 4
StockEnginePart3Unwind LABEL BYTE
    DB 021h,00h,00h,00h
    DD imagerel StockEngineSetPitch,imagerel StockEnginePitchPart2,imagerel StockEngineRootUnwind
.pdata SEGMENT READONLY
    DD imagerel StockEngineSetPitch,imagerel StockEnginePitchPart2,imagerel StockEngineRootUnwind
    DD imagerel StockEnginePitchPart2,imagerel StockEnginePitchPart3,imagerel StockEnginePart2Unwind
    DD imagerel StockEnginePitchPart3,imagerel StockEngineSetPitchEnd,imagerel StockEnginePart3Unwind
.pdata ENDS
END
