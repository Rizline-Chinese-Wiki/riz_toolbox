# Post-write endpoint world read review

Reviewed the four frozen candidate files under `work/line-ring-next-functional-20261113/candidate/` against the accepted 892-input parent/local writer. Manifest SHA-256: `42BDA5978831E1FE51B5CC0E89EA77AC72E12E6F5C8D68CA18BAA539593000EE`.

| Candidate file | SHA-256 |
| --- | --- |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs` | `8C169F71E7F915AB9E9350A19A2269C92D48C02DC267C0B539F7F10F42D008C6` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs` | `DA5367E10CEEEBCAEE08B1B0E1860E24E3244AC2B37FFAAAB165A4061BAB6CF7` |
| `src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs` | `47E0EB1D4E2522FD579CF767354CB04F2A72CBB4DAB0BB37902BAFE88A03DD79` |
| `tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs` | `7077D9F2E433753560F5DE0EFADF80F861C4202CC3574D0285718F4648BAB5E9` |

## Findings

No confirmed functional or lifetime defect was found in the four-file delta. `ReadWorld` verifies the writer profile and retained source, roots and checks the exact Transform, and invokes the source reader's saved/current `get_position` through its guarded value publication path. The source profile already pins and rechecks that getter. The new command observation runs in the adopted original source operation after a finished application and before restoration. It checks the selected parent/local targets, reads two complete parent/local/world vectors, compares every component by bits, rejects nonfinite world values, and rechecks the selected parent/local graph before returning data with the original source and command owner.

On observed tuple drift or a nonfinite world value, it marks the writer state unrecognized, preventing later setters or selected inverse. A callback that disposes the writer during a getter is stopped by the existing lifetime guards; the operation exception path retains the effects owner and original source after entered setters. The returned observation is historical, and the code explicitly marks it non-atomic and does not claim complete Seek permission.

## Verification boundary

This review did not build or run the five new cases (32–36). Their callback arming counts and the earliest nonfinite refusal path require the root-owned shadow run. The observation makes no atomicity or independent world-target claim. No UI, game process, account, installed runtime, or old archive was accessed.
