# Isolated Vector3 buffer primitive review

Reviewed file: `work/line-vector3-buffer-next-20261114/src/RizToolbox.Runtime/NativeRuntime.Vector3Buffer.cs`

SHA-256 reviewed: `AD0B55E7984D84FFA7145C8538C8762A41700494EC555CA54860D113CB9C0047`

Scope: read-only static correctness review against accepted `src/RizToolbox.Runtime/NativeRuntime.Vector3Array.cs`, `NativeRuntime.HoldGeometryReadContainers.cs`, `NativePublicationCall.cs`, `NativeRuntime.CanvasCacheArrayWrite.cs`, `RootHandle`, and `work/line-renderer-next-20261114/HANDOFF.md`. The isolated primitive was not built or exercised against a native backend in this review. A different source hash needs a fresh review.

## Blocking findings

None found for this exact revision. In particular, the temporary pin is registered before the first post-export caller guard; the original array, canonical class, rank, stride, official bounds, raw complete storage, and source callback are rechecked before a prefix store. The prefix writable check runs again after `beforeStore`, with no caller callback between that final check and `Marshal.Copy`. Cleanup exceptions retain the original root, store-entry flag, and available before/completed observation on the outer exception. Local page checks reject execute-only storage before raw reads and object-dereferencing native exports.

## Nonblocking findings and integration conditions

1. `prefix` is an `IReadOnlyList`, not necessarily an immutable collection (lines 47–59, 117–125). Caller guards and custom indexers can run while it is copied. A producer must supply owned, validated command bits or validate the captured bits under its own command ownership; validating a mutable source list before this call does not establish finiteness of the values ultimately copied. Raw nonfinite bits remain necessary for restoration, as the primitive documents.
2. The successful `After` observation describes this pinned array only (lines 20–25, 94–100, 230–248). The primitive checks `verifySource` before entry, but does not read back the current `LineControl.linePositions`, saved line array/count, renderer plan, or Unity consumer after the store. The downstream command owner must retain the original source/root/array/plan, journal entry before store, and perform current-field readback before claiming a renderer update. The handoff explicitly says `SetPositions(Vector3[], Int32)` can save fields before a later native exception.
3. The two complete passes and pre-store comparisons are historical observations, not a native compare-and-swap. Another writer can change and restore bytes between observations, and `Marshal.Copy` can have partial effects on failure. The file labels snapshots and conditional writes non-atomic and exposes `LineVector3BufferStoreEntered` on thrown post-entry paths; recovery must use that uncertainty and must not claim to undo external alias or Unity effects.

The earlier `PAGE_EXECUTE` readable-check gap was resolved in this reviewed SHA by `VerifyLineVectorBufferMemory` and the `V` wrapper. This review does not certify the still-unbuilt integration or renderer writer.
