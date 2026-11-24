option casemap:none
EXTERN RizWinHttpActiveSlots:QWORD
PUBLIC WinHttpOpen
PUBLIC WinHttpGetProxyForUrl
PUBLIC WinHttpGetIEProxyConfigForCurrentUser
PUBLIC WinHttpCloseHandle
.code
; Windows x64 typed tail forwarding: preserve all argument registers, stack
; arguments, caller return PC and LastError. Only volatile R11 is consumed.
WinHttpOpen PROC
    mov r11,QWORD PTR [RizWinHttpActiveSlots]
    jmp QWORD PTR [r11]
WinHttpOpen ENDP
WinHttpGetProxyForUrl PROC
    mov r11,QWORD PTR [RizWinHttpActiveSlots]
    jmp QWORD PTR [r11+8]
WinHttpGetProxyForUrl ENDP
WinHttpGetIEProxyConfigForCurrentUser PROC
    mov r11,QWORD PTR [RizWinHttpActiveSlots]
    jmp QWORD PTR [r11+16]
WinHttpGetIEProxyConfigForCurrentUser ENDP
WinHttpCloseHandle PROC
    mov r11,QWORD PTR [RizWinHttpActiveSlots]
    jmp QWORD PTR [r11+24]
WinHttpCloseHandle ENDP
END
