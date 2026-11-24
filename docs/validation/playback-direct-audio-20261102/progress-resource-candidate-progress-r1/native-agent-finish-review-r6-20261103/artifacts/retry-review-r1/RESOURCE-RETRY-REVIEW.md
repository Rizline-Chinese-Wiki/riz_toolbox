# Resource retry read-only review r1

UTC evidence capture: 2026-11-02T15:51:45Z.

Scope: the isolated candidate under `../source`; no product source was edited by this reviewer. The implementation contract was read before review. This review used source inspection only. It did not run a game, a game image, UI, account, test process, or native fixture. A functional regression must independently confirm the finding and its fix.

## Finding F-01 — Normal physical withdrawal permanently prevents resource-root retirement

Severity: P2, retained ResourceReference root after known shutdown. This is a lifetime/cleanup defect, not an authority bypass.

### Evidence

- E-01: `PlaybackProgressWatch.inc.snapshot:387-400`, particularly line 392, requires `owner->Flags == RtSourceArmed` before a not-yet-retired resource ticket can enter phase 5. It separately requires `!owner->Faults`, `!owner->InFlight`, and zero exact initializer physical responsibility.
- E-02: `PlaybackSourceContext.cpp.snapshot:569-578`, particularly line 577, implements normal disarm as `(owner->Flags & ~RtSourceArmed) | RtSourceWithdrawn`. Its state can never again equal `RtSourceArmed`; rearming an owner with Withdrawn is refused.
- E-03: `NativePlaybackStockSourceHub.cs.snapshot:136-143` invokes that disarm during `Withdraw()`. `ExactHookBroker.Playback.Group.cs.snapshot:365-369` exposes the normal group withdrawal path, while lines 347-357 preserve independently readable known activity and lines 333-337 continue pumping pending resource releases.
- E-04: `NativePlaybackStockSourceHub.ProgressResource.cs.snapshot:147-169` sets `_releaseRequested`, returns false when native retirement returns 0, and calls `_referenceRoot.Dispose()` only after native phase-5 evidence. The hub retains and repumps that ticket (lines 83-87), so the root is intentionally kept, but no future pump can satisfy E-01 after E-02.
- E-05: `PlaybackProgressWatch.inc.snapshot:144-158` permits ordinary watch retirement when physical work is zero, Faults is zero, and Unknown is absent. The resource ticket's extra Armed requirement is therefore inconsistent with the existing known-withdrawn watch drain policy.

### Path

1. Capture a real resource ticket through the retained Progress watch with a non-null ResourceReference, leaving its independent reference root retained.
2. Dispose/withdraw the physical playback group before disposing the ticket or before the initializer owner has released that ticket. An in-flight normal original may drain after withdrawal; either ordering reaches known zero physical work.
3. Normal group withdrawal clears Armed and sets Withdrawn. Unknown is absent, Faults is zero, and the actual owner/initializer responsibilities have physically exited.
4. Dispose the ticket, dispose/pump its Router/initializer, or pump an already release-requested ticket. Native retirement returns 0 solely because Armed is absent. The ticket cannot produce phase 5 and the managed reference root remains retained permanently.

### Suggested change and acceptance

Allow retirement for the exact known native owner after physical drain, including normal Withdrawn. Preserve refusal for Unknown, nonzero Faults, wrong owner thread, owner InFlight, and the ticket's exact initializer PhysicalInFlight. This must change retirement only; it must not revive capture or source authority.

Run a real owned-fixture managed regression that retains a non-null resource ticket, withdraws the group first, verifies known zero activity, then disposes/pumps the ticket/Router. Require phase-5 native retirement and the exact reference-root release. Repeat while an actual original is held: retain before physical exit and release after confirmed exit. Keep the existing Unknown and free-unknown negative checks, with their original evidence preserved.

## Other reviewed paths

- Native first-capture and initialization-handoff retries allocate a fresh opaque record and tombstone the old native receipt; no old graph or epoch is rewritten.
- `InitializationHandoff` late retry restores its retained predecessor session, while the new native receipt captures the current native logical session and allocates a later epoch at admission. No additional deterministic positive-path block was found there by inspection.
- Pending watch replacement binds the fresh initializer before replacement, marks only the exact replaced pending records retired, and late retirement compares receipt identity before clearing a pending pointer.
- Router pumping of an undisposed old candidate pumps observation responsibility only. After a successful replacement is staged, old candidate disposal retires its exact watches and releases seven roots only after known zero activity. Calling Dispose again through `_source` has an already-released early exit. No additional deterministic `_transferFailure` trigger was found in that normal path by inspection.
- The parent is separately correcting the known `FirstCaptureAdmission.Registered` old-candidate ownership rejection and preserving the candidate across repeated early interruptions. Those issues are not re-reported as new findings here.

These inspection results are not a functional pass for late/phase-6 retry. Fresh owned-fixture execution still needs to demonstrate the positive replacement/source-open/unique-Clock route.

## Snapshot integrity

| File | SHA-256 |
| --- | --- |
| `PlaybackProgressWatch.inc.snapshot` | `9a9ee0a1c0cb90887997d40a30940758226e7bef27ccaa00a5361fd5193ee7dd` |
| `PlaybackSourceContext.cpp.snapshot` | `e2da32c16cae926f325dd8ffe079abf42bdf1975f9458cbde5035a72e0281f4e` |
| `NativePlaybackStockSourceHub.ProgressResource.cs.snapshot` | `c58641089799a29b48183178392ef745fb24ddcedff898d9d25be3a7e0c6f169` |
| `NativePlaybackStockSourceHub.cs.snapshot` | `ae17ccfa141a1aaa5f767a20dc0c0479c10eea9153bc19a2a9102c456f9e9ca8` |
| `ExactHookBroker.Playback.Group.cs.snapshot` | `6bf8eac5bc07acb73e1e4e8df51ceea3ab6a125a3c2feb679f852ddf40e5b81a` |
