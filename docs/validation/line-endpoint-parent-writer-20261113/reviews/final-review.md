# Parent writer final code review — 2026-11-13

Scope: isolated `work/line-endpoint-parent-writer-20261113` parent writer, read against the accepted main reader/source design. No auditor build, test, game, account, environment, or Git action was performed. The passing 29-check `root-r2` byte of `NativeSeekLineEndpointCommands.cs` is preserved in `r2-before/` (SHA-256 `0A0CDF0E4BA5505D113B377278BC0A44D8B5B1F1C5FD9CA7F0E88927B52E3410`). The root reported `root-r2` build 0 warnings/0 errors and 29/29 checks for that earlier snapshot; its result SHA-256 is `34E20C6E9607FB684169DD928BF5CD01E048107F40388445CDF0CADEB7040B05`. That result does not validate the settled bytes below.

## Findings and resolution in the isolated candidate

1. **Blocking, resolved: cached Boolean/Void class identity could pass SetParent admission.** `LineEndpointClass` returns a cached class pointer, and `LineRingLayout`'s scalar canonical comparison reaches cached `Class(...)`. The prior parent inspector checked Boolean storage and Void return type but did not freshly verify either class's image, name, namespace, and canonical type. A changed class export with stable type name, type code, size, and class pointer could pass. The settled `NativeRuntime.LineEndpointParentMetadata.cs` calls `LineEndpointClassType` for Boolean and `VerifyLineEndpointVoid` for Void, and compares fresh/cached Boolean layout. `WriterParentCanonicalClassDrift` exercises warmed-cache image/name/namespace drift for both types (six cases; test file lines 640–705).

2. **Blocking, resolved: preparation accepted contradictory endpoint and parent-graph snapshots.** The previous `Prepare` saved endpoint parent A from `Read` and parent B from `ReadGraph` without correlating their common Transform. It could plan a SetParent order from B while later checking the selected endpoint from A. `CheckEndpointGraph` now requires each selected endpoint parent to equal its edge in the complete observed graph before `PlanParentOrder` and during `Observe` (`NativeSeekLineEndpointCommands.cs` lines 112–115, 239–254, 420–423). It checks the writer lifetime on entry, each row, and exit; a mismatch after preparation is sticky. `WriterParentGraphDisagreement` changes a selected parent only at the first graph read and requires constructor refusal before any setter (test lines 708–727).

3. **Moderate, resolved by inspection: an unsupported actual graph was not sticky after preparation.** `ReadGraphOnce` can reject a cycle, depth overflow, or Transform bound with `NotSupportedException`; the previous `ReadGraph` caught only observation conflicts. The settled `ReadGraph` marks the prepared writer unrecognized and rethrows `NotSupportedException` (`NativeSeekLineEndpointCommands.cs` lines 218–236). There is no separate 32nd test for this exact branch: the retained source verifier can reject such a graph first, and a reflected stub would mirror this small catch rather than test the real retained path.

## Settled-byte audit

| File under isolated candidate | SHA-256 |
| --- | --- |
| `src/RizToolbox.Runtime/NativeRuntime.LineEndpointParentMetadata.cs` | `B515E14E5587BC9B5FAFDF42FF5D68F25B013C562C63B3FC8B947CBC085386ED` |
| `src/RizToolbox.Runtime/NativeRuntime.LineEndpointPublication.cs` | `AE35B0DFFFECEFEF86923325A5D525B3CE8B17F8E2A52FBD66E99E5ED4562B2A` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionNativeProfile.cs` | `FA8A132356846FBC0B27DFE852B7BD2608B358C1C3B84B38259FE48C755EF15F` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs` | `C746A204E65F4EC4C08AC969B96162142215C88FF0E3355382C1FD3EF3E75313` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs` | `EAD31A9D71D868E117C7BEAE7F00044CA699C34038F78F4173A67BD2A0BCE98C` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.EndpointAccess.cs` | `7D3F0545B6D58A3DD9A2CF80E525EAF95544DFFA27DA03A7494161D0BC9A8F5F` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs` | `646C8505D6E2403C7E52AD33EA986F388927232368DC4B74D99627141C7A3735` |
| `src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs` | `23AC64780090974AA849D7930CE50A212B0722E1F801D9FA7996086DA5AB90CF` |
| `tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs` | `E779A94A995E2ABE2790EBA8EBAC84213F1F7041F9FEAFFA3F175D6856DC200B` |

No further blocking source finding in these bytes. The review checked exact Transform/Vector3/Single/Boolean/Void metadata, MethodInfo and pinned wrapper admission (SetParent RVA `0x1560D80`, 96 bytes, SHA-256 `BFCBDF4F759720FDC20BD1B1CBE40683AC00A170E56F3484B5A4ED1C63E9D28C`), guarded native inspection and source withdrawal, retained original-source ownership and unknown cleanup, complete graph bounds/cycles and intermediate parent order, entered-command reverse restoration, and the journal's unresolved Unity effects. `AtomicConditionalWrites`, `BeforeIsAtomicPreState`, and `CompleteSeekPermitted` remain false as required; finite native callbacks and engine effects remain open. Worst-case planning cost is a known deferred performance question, not a claim of this review. Fresh main/shadow build and all 31 checks are pending with the root agent.
