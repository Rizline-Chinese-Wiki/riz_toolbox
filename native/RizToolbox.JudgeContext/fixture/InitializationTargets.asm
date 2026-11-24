OPTION CASEMAP:NONE
EXTERN RizInitializationFixture_StartBody:PROC
EXTERN RizInitializationFixture_GetterBody:PROC
.code
; Separate, inspectable executable targets. Dobby relocates only NOPs and the
; unmodified typed tail jump keeps the actual original outside managed frames.
ALIGN 16
PUBLIC RizInitializationFixture_StartTarget
RizInitializationFixture_StartTarget PROC
    REPT 32
        nop
    ENDM
    jmp RizInitializationFixture_StartBody
RizInitializationFixture_StartTarget ENDP
ALIGN 16
PUBLIC RizInitializationFixture_GetterTarget
RizInitializationFixture_GetterTarget PROC
    REPT 32
        nop
    ENDM
    jmp RizInitializationFixture_GetterBody
RizInitializationFixture_GetterTarget ENDP
END
