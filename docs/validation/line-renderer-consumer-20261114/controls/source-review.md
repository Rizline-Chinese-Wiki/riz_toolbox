# Renderer consumer source review

Status: root static review recorded; tests-r6 focused checks passed; fresh-main acceptance remains pending. This is a root review, not an independent-agent PASS. The fixture agent stopped on a quota error; the pending review agent was cancelled before initialization. Neither agent supplies acceptance evidence.

Reviewed diff: `candidate-diff-current.patch`, SHA-256 `95b0814c0482af210c7334cd132ed8ae04acdfc436b723f5e9ac095cfa58861d`. The exact 18 C# file pairs are in `candidate-files-current.json`. Accepted main remains 913 inputs until an explicit payload application; the frozen candidate is 922 inputs.

## Contract and trace

| Evidence | Finding | Implementation path |
|---|---|---|
| Accepted SetPositions body @3fb160, 288 bytes, body SHA e948e88fdbba2a25281ae2fb8ced224a3b9197915714dfef210fdc19e86fb8e6 | Array@88 and count@90 are saved before the plan/Unity branch can fail. Admission alone does not prove native entry. | Original reader invokes the accepted exact profile; the same endpoint owner records distinct admission/entry/return flags and observes selected saved fields after an entered failure. |
| Fixed LineInformation/nested plan dump, `work/line-renderer-next-20261114/observed-metadata.json` | The nested plan is not UnityEngine.LineRenderer. A child target is declared UnityEngine.Component; the active consumer is exactly UnityEngine.LineRenderer. | Runtime enumerates the canonical nested class, exact declared fields/types/offsets/instance bounds. Reader retains plan/pool/child/target/Transform in the original deduplicated source table. |
| Original LineControl.linePositions@78 and producer @3ed4bc | All occurrences share one physical array. A culled occurrence does not submit count zero. | Original scratch prefix primitive is reused. Complete tail bits, all source Line saved aliases, ordered repeats and explicit shorter counts are journaled. No per-Line replacement array or entire producer call is introduced. |
| tests-r2 case 1 actual failure | Calling the public source diagnostics from an already held source operation reenters its managed gate. | New exact adopted-owner/private capture+Plan association check stays inside that existing operation; raw original Canvas accessors are borrowed without a second child/capture. |
| Root review of accessor factories | The existing AcquireChild contract permits one cache child and one position child only; duplicate acquisition must refuse before native work and preserve the original borrower. | The original cache/position access is retained with `??=`. Existing single-acquisition guards remain in force. Corrected case 11 checks duplicate refusal and subsequent original renderer use; it does not manufacture a second child. |
| Root review of final source guard; tests-r5 case 10 | A saved-field getter callback can mutate actual Canvas values after an earlier Canvas sample. | Complete actual Canvas values bracket world/scene/saved-field callbacks. This remains an ordered, non-atomic observation. The case rejects native dispatch and preserves the independent external Canvas mutation during scratch recovery. |
| Root review of reverse storage order | Generated writes are array then count, so selected inverse is count then array. An original count can temporarily exceed the current scratch length during that inverse. | Baseline array/count bounds are checked once before effects; reader observes raw count during recovery. The one forward sequence is reversed across SetPositions fields and scratch prefixes. Case 12 covers a longer original saved array. |

## Responsibilities and refusal rules

- The existing endpoint owner and source child retain every admitted renderer operation. Only confirmed actual entry joins the inverse sequence. A matching externally written target without own entry is quarantined.
- Selected inverse uses official GC-aware field exports for count/reference, then the original raw prefix primitive in global reverse order. It never replays SetPositions or attempts to undo Unity rendering.
- Full scratch bits and the complete selected saved-state/consumer association vector must match the last recognized state before another store. Unknown state is sticky; unknown native cleanup forbids further native work or retries.
- Owner-thread admission precedes callbacks. Preparation is reserved before profile/Canvas callbacks; manual scratch mutation cannot interleave. Callback disposal defers release until the held operation ends and retains entered effects.
- A renderer preflight with no admitted effect does not prevent independent selected endpoint recovery.

## Validation history

- compile-r1: six-component compile, W0/E0, original tool session 8039 actual exit 0; no renderer tests.
- tests-r1: compile passed; 0/8 tests because a synthetic class-parent override omitted the existing Canvas KeyPoint List relationship. Fixed in the fixture; not a product acceptance.
- tests-r2: compile passed; case 1 exposed the public-source diagnostic reentry above. Original session 83652 actual exit 1.
- tests-r3: compile passed; case 1 completed apply but failed its different-count expectation. Both synthetic geometries demanded the same sample count. The revised fixture uses actual parent TRS/point coordinates to produce 101/6/101 without replacing native getters. Original session 90714 actual exit 1.
- tests-r4: case 1 passed distinct-Line/shared-scratch/order/tail/selected recovery. The remaining predecessor run was explicitly cancelled after the extra final Canvas verification was added; `root-cancellation.json` records exact matched controller/child identities. Interrupted case 2 is not a natural pass. Original session 42208 actual exit 1 from controller termination.
- tests-r5: six/Judgements W0E0; ten cases (10,9,8,7,6,5,4,3,2,1) passed. Case 11 failed because the test incorrectly expected duplicate Canvas child acquisition to succeed; the existing single-acquisition contract correctly rejected it before native work. Case 12 was not run. Original session 36642 actual exit 1/chunk c706c2. Production sources did not change for the repair.
- tests-r6: current 922 graph, focused corrected cases 11 and 12 only, controller 40324/original session 13286. This focused run passed naturally: six-component and Judgements builds W0/E0, cases 11/12 each exit 0, unchanged 922 frozen inputs/current overlay. Original root session 13286 actual exit 0/chunk 0cc7d2; its separate root witness binds the final result. The applied main must still rebuild and execute all twelve cases fresh. The ten predecessor passes are history, not acceptance of the new test assembly.

The controls for compile-r1/tests-r1 were reconstructed from the retained later control by reversing only the per-case selector change, then matched exactly to their original recorded SHA 7220b5f677013c68572b27a95a143c0cbdcced8a1af0f373002254a94a0a2bdf. tests-r2/tests-r3 controls are exact copies with recorded SHA 3e134ed947e875c1e539c6f35f9eb7dec729b10fe1ba52c5f38f98eaa597cae4. Later runs copy controls before execution.

## Open scope

No actual GameAssembly/body admission or Unity render closure is tested here; the synthetic SEC_IMAGE executes no mapped machine code. Static/group-only producer behavior, other producer scalar writes, canonical Ring seed, Hold engine, actual DSP/pitch and the complete clock transaction remain open. AtomicSnapshot, ExternalConsumersClosed and CompleteSeekPermitted remain false. No installation/game/UI/account/proxy/Git/old-ZIP operation is part of this batch; the user README bytes are preserved.
