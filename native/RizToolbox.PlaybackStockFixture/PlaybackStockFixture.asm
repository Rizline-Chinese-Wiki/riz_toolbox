option casemap:none
option dotname
EXTERN StockGetTimeBody:PROC
EXTERN StockSetPitchBody:PROC
IFDEF RIZ_STOCK_LOWER_PITCH_PROFILE
EXTERN StockLowerPitchCache:QWORD
ENDIF
IFDEF RIZ_STOCK_LOWER_TIME_PROFILE
EXTERN StockLowerTimeCache:QWORD
ENDIF
EXTERN StockSetTimeBody:PROC
EXTERN StockPauseBody:PROC
EXTERN StockReplayBody:PROC
EXTERN StockResumeBody:PROC
EXTERN StockPlayScheduledBody:PROC
EXTERN StockSetClipBody:PROC
EXTERN StockProgressPlayBody:PROC
EXTERN StockPauseLeafBody:PROC
EXTERN StockPlayerConstructorBefore:PROC
EXTERN StockPlayerConstructorAfter:PROC
EXTERN StockAudioPlayHelperBody:PROC
PUBLIC StockAudioPlayHelper, StockAudioPlayHelperEnd
EXTERN StockAudioPlayDelayedBody:PROC
PUBLIC StockAudioPlayDelayed, StockAudioPlayDelayedEnd
EXTERN StockAudioPlayBody:PROC
PUBLIC StockAudioPlay, StockAudioPlayEnd
EXTERN StockAudioStopBody:PROC
PUBLIC StockAudioStop, StockAudioStopEnd
EXTERN StockAudioStopBooleanBody:PROC
PUBLIC StockAudioStopBoolean, StockAudioStopBooleanEnd
EXTERN StockAudioSetLoopBody:PROC
PUBLIC StockAudioSetLoop, StockAudioSetLoopEnd
EXTERN StockFixtureInitialized:BYTE
PUBLIC StockGetTime, StockGetTimeEnd, StockSetPitch, StockSetPitchEnd
PUBLIC StockPause, StockReplay, StockResume, StockPlayScheduled, StockSetClip, StockProgressPlay, StockPauseLeaf
PUBLIC StockPauseEnd, StockReplayEnd, StockResumeEnd, StockPlayScheduledEnd, StockSetClipEnd, StockProgressPlayEnd, StockPauseLeafEnd
PUBLIC StockSetTime, StockSetTimeEnd
PUBLIC StockPlayerConstructor, StockPlayerConstructorEnd
PUBLIC StockExecutableDir64Anchor
.code
ALIGN 16
StockGetTime PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,50h
    mov rbx,rcx
    call StockGetTimeBody
    mov rbx,qword ptr [rsp+60h]
    add rsp,50h
    pop rdi
    ret
    DB 640-($-StockGetTime) DUP(090h)
StockGetTime ENDP
StockGetTimeEnd LABEL BYTE

; Explicit 40 prefix pins the actual stock SetPitch six-byte prologue.
; Its independent unwind is PUSH RBX / ALLOC_SMALL 30h, not GetTime's.
ALIGN 16
StockSetPitch PROC
    DB 040h
    push rbx
    sub rsp,30h
    mov rbx,rcx
IFDEF RIZ_STOCK_LOWER_PITCH_PROFILE
    ; The new two-image fixture genuinely tail-jumps to a two-argument entry.
    ; Only the wrapper has a MethodInfo argument; record it here, never invent
    ; one from the lower entry's unused R8 register.
    mov qword ptr [rcx+50h],r8
    mov rax,qword ptr [StockLowerPitchCache]
    add rsp,30h
    pop rbx
    jmp rax
ELSE
    call StockSetPitchBody
    add rsp,30h
    pop rbx
    ret
ENDIF
    DB 320-($-StockSetPitch) DUP(090h)
StockSetPitch ENDP
StockSetPitchEnd LABEL BYTE

ALIGN 16
StockPause PROC
    DB 040h
    push rbx
    sub rsp,20h
    mov rbx,rcx
    call StockPauseBody
    add rsp,20h
    pop rbx
    ret
    DB 160-($-StockPause) DUP(090h)
StockPause ENDP
StockPauseEnd LABEL BYTE

; Replay has its own RBX/40 frame, XMM6 at +30, then a real late RDI
; store in a chained RF. The body calls the actual pitch/schedule entries.
ALIGN 16
StockReplay PROC
    DB 040h
    push rbx
    sub rsp,40h
    cmp byte ptr [StockFixtureInitialized],0
    DB 16-($-StockReplay) DUP(090h)
    movaps xmmword ptr [rsp+30h],xmm6
    mov rbx,rcx
    movaps xmm6,xmm1
    DB 152-($-StockReplay) DUP(090h)
StockReplayLate LABEL BYTE
    mov qword ptr [rsp+50h],rdi
    mov rdi,rcx
    call StockReplayBody
    mov rdi,qword ptr [rsp+50h]
    movaps xmm6,xmmword ptr [rsp+30h]
    jmp near ptr StockReplayEpilog
    DB 400-($-StockReplay) DUP(090h)
StockReplayEpilog LABEL NEAR
    add rsp,40h
    pop rbx
    ret
StockReplayReturnEnd LABEL BYTE
    DB 560-($-StockReplay) DUP(090h)
StockReplay ENDP
StockReplayEnd LABEL BYTE

; Resume's primary RF differs: RDI/50, XMM6 at +40, and RBX saved
; only at offset72h. The next chained RF also really saves XMM7.
ALIGN 16
StockResume PROC
    DB 040h
    push rdi
    sub rsp,50h
    cmp byte ptr [StockFixtureInitialized],0
    DB 16-($-StockResume) DUP(090h)
    movaps xmmword ptr [rsp+40h],xmm6
    DB 109-($-StockResume) DUP(090h)
    mov qword ptr [rsp+60h],rbx
    mov rdi,rcx
    mov rbx,r8
    movaps xmm6,xmm1
    DB 245-($-StockResume) DUP(090h)
StockResumeLate LABEL BYTE
    movaps xmmword ptr [rsp+30h],xmm7
    movaps xmm7,xmm6
    call StockResumeBody
    movaps xmm7,xmmword ptr [rsp+30h]
    mov rbx,qword ptr [rsp+60h]
    movaps xmm6,xmmword ptr [rsp+40h]
    jmp near ptr StockResumeEpilog
    DB 538-($-StockResume) DUP(090h)
StockResumeEpilog LABEL NEAR
    add rsp,50h
    pop rdi
    ret
StockResumeReturnEnd LABEL BYTE
    DB 720-($-StockResume) DUP(090h)
StockResume ENDP
StockResumeEnd LABEL BYTE

; The actual Double1 leaf has a late XMM6 save in a chained RF. Its own
; controlled body receives the unmodified XMM1 double and incoming R8 MI.
ALIGN 16
StockPlayScheduled PROC
    DB 040h
    push rbx
    sub rsp,30h
    DB 21-($-StockPlayScheduled) DUP(090h)
StockPlayScheduledLate LABEL BYTE
    movaps xmmword ptr [rsp+20h],xmm6
    mov rbx,rcx
    movaps xmm6,xmm1
    call StockPlayScheduledBody
    movaps xmm6,xmmword ptr [rsp+20h]
    jmp short StockPlayScheduledEpilog
    DB 76-($-StockPlayScheduled) DUP(090h)
StockPlayScheduledEpilog LABEL NEAR
    add rsp,30h
    pop rbx
    ret
StockPlayScheduledReturnEnd LABEL BYTE
    DB 96-($-StockPlayScheduled) DUP(090h)
StockPlayScheduled ENDP
StockPlayScheduledEnd LABEL BYTE

ALIGN 16
StockSetClip PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,20h
    mov rdi,rcx
    mov rbx,rdx
    call StockSetClipBody
    mov rbx,qword ptr [rsp+30h]
    add rsp,20h
    pop rdi
    ret
    DB 80-($-StockSetClip) DUP(090h)
StockSetClip ENDP
StockSetClipEnd LABEL BYTE

; CMP is part of the complete seven-byte move. The very next JE consumes
; its flags after the independent R11 continuation, before any flag writer.
ALIGN 16
StockProgressPlay PROC
    sub rsp,28h
    cmp byte ptr [rcx+42h],dl
    je short StockProgressPlayEqual
    xor r9d,r9d
    jmp short StockProgressPlayLate
    DB 31-($-StockProgressPlay) DUP(090h)
StockProgressPlayLate LABEL NEAR
    mov qword ptr [rsp+20h],rbx
    mov rbx,rcx
    call StockProgressPlayBody
    mov rbx,qword ptr [rsp+20h]
    add rsp,28h
    ret
    DB 100-($-StockProgressPlay) DUP(090h)
StockProgressPlayEqual LABEL NEAR
    mov r9d,1
    call StockProgressPlayBody
    add rsp,28h
    ret
StockProgressPlayReturnEnd LABEL BYTE
    DB 272-($-StockProgressPlay) DUP(090h)
StockProgressPlay ENDP
StockProgressPlayEnd LABEL BYTE

ALIGN 16
StockPauseLeaf PROC
    DB 040h
    push rbx
    sub rsp,20h
    mov rbx,rcx
    call StockPauseLeafBody
    add rsp,20h
    pop rbx
    ret
    DB 64-($-StockPauseLeaf) DUP(090h)
StockPauseLeaf ENDP
StockPauseLeafEnd LABEL BYTE

ALIGN 16
StockSetTime PROC
    DB 040h
    push rbx
    sub rsp,30h
    mov rbx,rcx
IFDEF RIZ_STOCK_LOWER_TIME_PROFILE
    mov qword ptr [rcx+88h],r8
    mov rax,qword ptr [StockLowerTimeCache]
    add rsp,30h
    pop rbx
    jmp rax
ELSE
    call StockSetTimeBody
    add rsp,30h
    pop rbx
    ret
ENDIF
    DB 80-($-StockSetTime) DUP(090h)
StockSetTime ENDP
StockSetTimeEnd LABEL BYTE

; Owned shared constructor: its first six bytes, 0x1B reference store,
; non-leaf unwind and raw RCX/RDX/R8 calling convention are independently
; executable. The before/after calls expose the real store boundary to tests.
ALIGN 16
StockPlayerConstructor PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,20h
    mov rbx,rcx
    mov rdi,rdx
    mov qword ptr [rsp+40h],r8
    nop
    call StockPlayerConstructorBefore
    mov qword ptr [rbx+10h],rdi
    mov rcx,rbx
    mov rdx,rdi
    mov r8,qword ptr [rsp+40h]
    call StockPlayerConstructorAfter
    mov rbx,qword ptr [rsp+30h]
    add rsp,20h
    pop rdi
    ret
    DB 64-($-StockPlayerConstructor) DUP(090h)
StockPlayerConstructor ENDP
StockPlayerConstructorEnd LABEL BYTE

ALIGN 16
StockAudioPlayHelper PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,20h
    call StockAudioPlayHelperBody
    mov rbx,qword ptr [rsp+30h]
    add rsp,20h
    pop rdi
    ret
    DB 80-($-StockAudioPlayHelper) DUP(090h)
StockAudioPlayHelper ENDP
StockAudioPlayHelperEnd LABEL BYTE

ALIGN 16
StockAudioPlayDelayed PROC
    DB 040h
    push rbx
    sub rsp,30h
    call StockAudioPlayDelayedBody
    add rsp,30h
    pop rbx
    ret
    DB 80-($-StockAudioPlayDelayed) DUP(090h)
StockAudioPlayDelayed ENDP
StockAudioPlayDelayedEnd LABEL BYTE

ALIGN 16
StockAudioPlay PROC
    DB 040h
    push rbx
    sub rsp,20h
    call StockAudioPlayBody
    add rsp,20h
    pop rbx
    ret
    DB 64-($-StockAudioPlay) DUP(090h)
StockAudioPlay ENDP
StockAudioPlayEnd LABEL BYTE

ALIGN 16
StockAudioStop PROC
    DB 040h
    push rbx
    sub rsp,20h
    call StockAudioStopBody
    add rsp,20h
    pop rbx
    ret
    DB 64-($-StockAudioStop) DUP(090h)
StockAudioStop ENDP
StockAudioStopEnd LABEL BYTE

ALIGN 16
StockAudioStopBoolean PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,20h
    call StockAudioStopBooleanBody
    mov rbx,qword ptr [rsp+30h]
    add rsp,20h
    pop rdi
    ret
    DB 80-($-StockAudioStopBoolean) DUP(090h)
StockAudioStopBoolean ENDP
StockAudioStopBooleanEnd LABEL BYTE

ALIGN 16
StockAudioSetLoop PROC
    mov qword ptr [rsp+8],rbx
    push rdi
    sub rsp,20h
    call StockAudioSetLoopBody
    mov rbx,qword ptr [rsp+30h]
    add rsp,20h
    pop rdi
    ret
    DB 80-($-StockAudioSetLoop) DUP(090h)
StockAudioSetLoop ENDP
StockAudioSetLoopEnd LABEL BYTE

; A real absolute address in executable image bytes.  Keep it outside every
; callable body/unwind range so the source ABI fixtures remain unchanged.
ALIGN 8
StockExecutableDir64Anchor LABEL QWORD
    DQ OFFSET StockSetPitch

.xdata SEGMENT READONLY
ALIGN 4
StockGetTimeUnwind LABEL BYTE
    DB 1,10,4,0,10,034h,12,0,10,092h,6,070h
StockSetPitchUnwind LABEL BYTE
    DB 1,6,2,0,6,052h,2,030h
StockPauseUnwind LABEL BYTE
    DB 1,6,2,0,6,032h,2,030h
StockSetClipUnwind LABEL BYTE
    DB 1,10,4,0,10,034h,6,0,10,032h,6,070h
StockPlayerConstructorUnwind LABEL BYTE
    DB 1,10,4,0,10,034h,6,0,10,032h,6,070h
StockReplayUnwind LABEL BYTE
    DB 1,21,4,0,21,068h,3,0,6,072h,2,030h
StockReplayLateUnwind LABEL BYTE
    DB 021h,5,2,0,5,074h,10,0
    DD IMAGEREL StockReplay, IMAGEREL StockReplayLate, IMAGEREL StockReplayUnwind
StockReplayEpilogUnwind LABEL BYTE
    DB 021h,0,0,0
    DD IMAGEREL StockReplay, IMAGEREL StockReplayLate, IMAGEREL StockReplayUnwind
StockResumeUnwind LABEL BYTE
    DB 1,114,6,0,114,034h,12,0,21,068h,4,0,6,092h,2,070h
StockResumeLateUnwind LABEL BYTE
    DB 021h,5,2,0,5,078h,3,0
    DD IMAGEREL StockResume, IMAGEREL StockResumeLate, IMAGEREL StockResumeUnwind
StockResumeEpilogUnwind LABEL BYTE
    DB 021h,0,0,0
    DD IMAGEREL StockResume, IMAGEREL StockResumeLate, IMAGEREL StockResumeUnwind
StockProgressPlayUnwind LABEL BYTE
    DB 1,4,1,0,4,042h,0,0
StockProgressPlayLateUnwind LABEL BYTE
    DB 021h,5,2,0,5,034h,4,0
    DD IMAGEREL StockProgressPlay, IMAGEREL StockProgressPlayLate, IMAGEREL StockProgressPlayUnwind
StockProgressPlayEqualUnwind LABEL BYTE
    DB 021h,0,0,0
    DD IMAGEREL StockProgressPlay, IMAGEREL StockProgressPlayLate, IMAGEREL StockProgressPlayUnwind
StockPlayScheduledUnwind LABEL BYTE
    DB 1,6,2,0,6,052h,2,030h
StockPlayScheduledLateUnwind LABEL BYTE
    DB 021h,5,2,0,5,068h,2,0
    DD IMAGEREL StockPlayScheduled, IMAGEREL StockPlayScheduledLate, IMAGEREL StockPlayScheduledUnwind
StockPlayScheduledEpilogUnwind LABEL BYTE
    DB 021h,0,0,0
    DD IMAGEREL StockPlayScheduled, IMAGEREL StockPlayScheduledLate, IMAGEREL StockPlayScheduledUnwind
.xdata ENDS
.pdata SEGMENT READONLY
ALIGN 4
    DD IMAGEREL StockGetTime, IMAGEREL StockGetTimeEnd, IMAGEREL StockGetTimeUnwind
    DD IMAGEREL StockSetPitch, IMAGEREL StockSetPitchEnd, IMAGEREL StockSetPitchUnwind
    DD IMAGEREL StockPause, IMAGEREL StockPauseEnd, IMAGEREL StockPauseUnwind
    DD IMAGEREL StockReplay, IMAGEREL StockReplayLate, IMAGEREL StockReplayUnwind
    DD IMAGEREL StockReplayLate, IMAGEREL StockReplayEpilog, IMAGEREL StockReplayLateUnwind
    DD IMAGEREL StockReplayEpilog, IMAGEREL StockReplayReturnEnd, IMAGEREL StockReplayEpilogUnwind
    DD IMAGEREL StockResume, IMAGEREL StockResumeLate, IMAGEREL StockResumeUnwind
    DD IMAGEREL StockResumeLate, IMAGEREL StockResumeEpilog, IMAGEREL StockResumeLateUnwind
    DD IMAGEREL StockResumeEpilog, IMAGEREL StockResumeReturnEnd, IMAGEREL StockResumeEpilogUnwind
    DD IMAGEREL StockPlayScheduled, IMAGEREL StockPlayScheduledLate, IMAGEREL StockPlayScheduledUnwind
    DD IMAGEREL StockPlayScheduledLate, IMAGEREL StockPlayScheduledEpilog, IMAGEREL StockPlayScheduledLateUnwind
    DD IMAGEREL StockPlayScheduledEpilog, IMAGEREL StockPlayScheduledReturnEnd, IMAGEREL StockPlayScheduledEpilogUnwind
    DD IMAGEREL StockSetClip, IMAGEREL StockSetClipEnd, IMAGEREL StockSetClipUnwind
    DD IMAGEREL StockProgressPlay, IMAGEREL StockProgressPlayLate, IMAGEREL StockProgressPlayUnwind
    DD IMAGEREL StockProgressPlayLate, IMAGEREL StockProgressPlayEqual, IMAGEREL StockProgressPlayLateUnwind
    DD IMAGEREL StockProgressPlayEqual, IMAGEREL StockProgressPlayReturnEnd, IMAGEREL StockProgressPlayEqualUnwind
    DD IMAGEREL StockPauseLeaf, IMAGEREL StockPauseLeafEnd, IMAGEREL StockPauseUnwind
    DD IMAGEREL StockSetTime, IMAGEREL StockSetTimeEnd, IMAGEREL StockSetPitchUnwind
    DD IMAGEREL StockPlayerConstructor, IMAGEREL StockPlayerConstructorEnd, IMAGEREL StockPlayerConstructorUnwind
    DD IMAGEREL StockAudioPlayHelper, IMAGEREL StockAudioPlayHelperEnd, IMAGEREL StockSetClipUnwind
    DD IMAGEREL StockAudioPlayDelayed, IMAGEREL StockAudioPlayDelayedEnd, IMAGEREL StockSetPitchUnwind
    DD IMAGEREL StockAudioPlay, IMAGEREL StockAudioPlayEnd, IMAGEREL StockPauseUnwind
    DD IMAGEREL StockAudioStop, IMAGEREL StockAudioStopEnd, IMAGEREL StockPauseUnwind
    DD IMAGEREL StockAudioStopBoolean, IMAGEREL StockAudioStopBooleanEnd, IMAGEREL StockSetClipUnwind
    DD IMAGEREL StockAudioSetLoop, IMAGEREL StockAudioSetLoopEnd, IMAGEREL StockSetClipUnwind
.pdata ENDS
END
