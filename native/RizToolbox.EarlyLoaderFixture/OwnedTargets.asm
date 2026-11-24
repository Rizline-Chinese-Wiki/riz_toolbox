OPTION CASEMAP:NONE
EXTERN OwnedEarlyStartBody:PROC
EXTERN OwnedEarlyExecuteBody:PROC
EXTERN OwnedEarlyReleaseBody:PROC
EXTERN OwnedEarlyDetachBody:PROC

; Ordinary importer exports with inspectable, independent code ranges. The
; producer relocates only these 32 actual NOP bytes. The tail jump preserves
; the four real native argument/return contracts without another stack frame.
PATCHABLE_ENTRY MACRO entryName:req, bodyName:req
    ALIGN 16
    PUBLIC entryName
    entryName PROC
        REPT 32
            nop
        ENDM
        jmp bodyName
    entryName ENDP
ENDM

.code
PATCHABLE_ENTRY OwnedEarlyStart, OwnedEarlyStartBody
PATCHABLE_ENTRY OwnedEarlyExecute, OwnedEarlyExecuteBody
PATCHABLE_ENTRY OwnedEarlyRelease, OwnedEarlyReleaseBody
PATCHABLE_ENTRY OwnedEarlyDetach, OwnedEarlyDetachBody
END
