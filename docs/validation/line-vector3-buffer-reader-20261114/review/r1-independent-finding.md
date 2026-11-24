# Reader/source/endpoint candidate review

Read-only static review of the 7 source files and 5 new line-buffer cases in
`work/line-vector3-buffer-reader-next-20261114`. No build or native/game run was
performed by this reviewer.

## Blocking finding

**An inverse can overwrite a callback's value when this writer never stored.**
`NativeSeekLineEndpointCommands.LineBuffer.cs:217` adds an Apply command to
`_lineBufferApplyEntered` during `BeforeStore`, before Runtime's final complete
prestate comparison and `Marshal.Copy`. A guard/source callback can set the
complete physical array to the planned `command.Target`; Runtime then rejects
the stale prestate with `LineVector3BufferStoreEntered=false`. The command layer
sets `TargetObserved=true` and accepts that value as the expected state at
lines 175-186. `RestoreOriginalLinePositionsBuffer` subsequently iterates the
admitted Apply index at lines 266-276 and writes `command.Before` over this
callback-owned value. The exception and journal distinguish admission from
actual store, so inverse eligibility should use confirmed store entry; a
target observation with no own store should remain external/unknown rather
than become an inverse target. The five new cases do not exercise this path.

## Other observations

- The candidate retains the adopted source after an admitted store, including
  selected restoration. Before-store unknown temporary cleanup propagates via
  `NativePublicationCleanupFailure` to the shared basis and endpoint owner.
- A post-store unknown temporary free is not among the five cases. Runtime's
  exception carries `LineVector3BufferWriteObservation`; the endpoint journal
  marks `StoreEntered` but leaves `StoreReturned/TargetObserved/Readback` false
  because source-level completion did not return. That is conservative, but a
  focused failure case should verify owner retention and no retry.
- History records copy the command list and expose read-only full-array bit
  snapshots. Thread and reentry checks occur before source/native entry; the
  existing endpoint writer tests cover the shared owner mechanics, while the
  five new cases do not invoke the buffer methods from a worker/reentrant call.

## SHA256 of reviewed candidate

| File (under candidate root) | SHA256 |
| --- | --- |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineBufferAccess.cs` | `8221D0B0A85B3F623D99A7E69B6DCC8C1B697E437F41C18D84B999F43F5F613A` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs` | `BE4DC402295E9C2FE7028EE883500CE1215CA80BBD2187A6EA6E9DC27007B8FB` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.LineBuffer.cs` | `6B042C273292C4DECE670C5FA01A0EFF6FCD7C267E522B063F58D651D232195C` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.EndpointAccess.cs` | `2A1949D5491FC4F350AEFD54A884D1E1DDD037D64EF464599EACA8A11CD38CB9` |
| `src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingBufferAccess.cs` | `A0C7114E8D5A7BCF92BF8793C684A30319FB7B2EA89F66DE13F41DA4BDFA3E25` |
| `src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingSource.cs` | `6D85C904B8A806E526568B6DA680210C4038F618A52F5A9B8B1C795E0523C1B0` |
| `src/RizToolbox.Runtime/NativeRuntime.LineRingSourceMetadata.cs` | `D0BC2FD15F87515519490BFDB8016FE5823CC974FF84DB9585AE1D9DBAFBB64E` |
| `tests/RizToolbox.Judgements.Tests/Program.cs` | `EF5DFAA4EA17B3F88890EA50B736484F7BFBC49326AB423969C32EC6E7406B10` |
| `tests/RizToolbox.Judgements.Tests/LineRingSource/FakeNative.LineRingBuffer.cs` | `110F54F8768A731C76AF5F8FDA4BEC4B2CA9AE6646BF5FB15E594EB2E00C7B7F` |
| `tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.LineBuffer.cs` | `7E0EB52AD6E2621E343DD7BD110EEA1CE5072CFD0328E80A4945129124EA9C3B` |
