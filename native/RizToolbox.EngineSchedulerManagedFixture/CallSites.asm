; Genuine CALL sites whose returned PCs are retained by the managed row ledger.
; Targets are ordinary owned fixture functions, never game code.
OPTION CASEMAP:NONE
EXTERN RtSchedulerManaged_CallbackEntry:QWORD
EXTERN RtSchedulerManaged_ReleaseEntry:QWORD
EXTERN RtSchedulerManaged_CallbackRoute:DWORD
EXTERN RtSchedulerManaged_CleanupRoute:DWORD
.code
PUBLIC RtSchedulerManaged_CallbackTarget
PUBLIC RtSchedulerManaged_PumpCallbackReturn
PUBLIC RtSchedulerManaged_RepeatCallbackReturn
PUBLIC RtSchedulerManaged_DrainCallbackReturn
RtSchedulerManaged_CallbackTarget PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    cmp dword ptr [RtSchedulerManaged_CallbackRoute], 1
    je RepeatCall
    cmp dword ptr [RtSchedulerManaged_CallbackRoute], 2
    je DrainCall
    call qword ptr [RtSchedulerManaged_CallbackEntry]
RtSchedulerManaged_PumpCallbackReturn LABEL NEAR
    jmp CallbackDone
RepeatCall:
    call qword ptr [RtSchedulerManaged_CallbackEntry]
RtSchedulerManaged_RepeatCallbackReturn LABEL NEAR
    jmp CallbackDone
DrainCall:
    call qword ptr [RtSchedulerManaged_CallbackEntry]
RtSchedulerManaged_DrainCallbackReturn LABEL NEAR
CallbackDone:
    add rsp, 28h
    ret
RtSchedulerManaged_CallbackTarget ENDP
PUBLIC RtSchedulerManaged_CleanupTarget
PUBLIC RtSchedulerManaged_RemoveCleanupReturn
PUBLIC RtSchedulerManaged_PumpCleanupReturn
PUBLIC RtSchedulerManaged_DrainCleanupReturn
RtSchedulerManaged_CleanupTarget PROC FRAME
    sub rsp, 28h
    .allocstack 28h
    .endprolog
    cmp dword ptr [RtSchedulerManaged_CleanupRoute], 1
    je RemoveCall
    cmp dword ptr [RtSchedulerManaged_CleanupRoute], 2
    je DrainCleanupCall
    call qword ptr [RtSchedulerManaged_ReleaseEntry]
RtSchedulerManaged_PumpCleanupReturn LABEL NEAR
    jmp CleanupDone
RemoveCall:
    call qword ptr [RtSchedulerManaged_ReleaseEntry]
RtSchedulerManaged_RemoveCleanupReturn LABEL NEAR
    jmp CleanupDone
DrainCleanupCall:
    call qword ptr [RtSchedulerManaged_ReleaseEntry]
RtSchedulerManaged_DrainCleanupReturn LABEL NEAR
CleanupDone:
    add rsp, 28h
    ret
RtSchedulerManaged_CleanupTarget ENDP
END
