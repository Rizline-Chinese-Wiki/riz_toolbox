# Hold judgement identity correction — 2026-10-20

## Verified defect

Frozen runtime evidence: `../runtime-visual-scope-cleanup-20261020/session-auto-return-failure/events-000001.jsonl`.
The first identity withdrawal (event 689) is `ReadHoldNote → RetainExactObject → VerifyReadable`.
At withdrawal there were 281 SDK observations, including 59 hold tails and one successful `auto.challenge-max`; the latter is not the failing source.
The final inner-hook count of 479 is not an identity coverage count.

`hold-tail-register-proof.txt` contains instructions read from the local, fingerprint-pinned GameAssembly:
`853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`.

- `CheckHold.MoveNext` is RVA `402670` (metadata return `System.Boolean`, no explicit parameters).
- `40268C mov rsi,rcx` initially retains the coroutine receiver.
- `402726 mov rdi,[rsi+20]` retains the owning `NoteJudgeControl`.
- The challenge-tail visual-effect path executes **`4030AA mov esi,[rax+8]`**, replacing RSI with the Vector3.Z bit pattern.
- `403186 jmp 40337D` joins the shared tail; `403388 call 3E8030` has return RVA `40338D`, the existing `hold.end` source.

Therefore RSI is not a valid coroutine receiver on every branch reaching that caller. The previous unconditional root attempt can fail exactly as seen in the frozen stack. Saved native registers were not logged at that live failure, so the individual runtime RSI value is not claimed.

## Implemented correction

`MoveNext` now has a separate exact metadata- and RVA-validated Boolean observer. The native delegate returns `byte` to preserve AL without the default four-byte managed `bool` marshalling assumption. It forwards the original receiver, MethodInfo and return byte unchanged and calls the original once.

At entry, the real coroutine is independently class-validated and rooted. Both hold sources obtain the coroutine from that in-flight context, then retain the existing exact owner, run, note and note-data checks. There is no fallback to RSI or a guessed pointer.

Every Boolean native entry, including suppressed reentry, increments thread-local depth. A nested judgement whose MoveNext context was suppressed cannot borrow the outer coroutine. Withdrawal denies future context reads immediately but releases an existing root only when that original invocation unwinds. Unknown sources and identity failures continue to withdraw the run.

## Checkpoint / validation

- Source and eight targeted offline regression cases are saved.
- Static disassembly has been generated from the pinned local DLL.
- Compilation and execution are delegated to the root agent's serial test/build slot; this checkpoint itself does not claim those checks passed.
- No game launch, installation or live completion claim was performed by this agent. A future live challenge-tail invocation is still needed to confirm the fix against the real runtime.
