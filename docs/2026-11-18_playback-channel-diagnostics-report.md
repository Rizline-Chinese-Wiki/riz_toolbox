# Playback channel and background diagnostics integration

The current R06 channel checks and R07 background diagnostics passed one serial Release integration run. This is offline acceptance of the controlled paths, with no game installation, launch, UI or account operation. R06, R07, R08, complete B01–B19 and production admission remain incomplete.

The immutable execution record is [validation-r8.json](../work/runtime-redesign-channel-operation-20261118/validation-r8.json), which links the complete process, source, assembly and native-input records under `run-r8-integrated-20261118`. Execution ran from 2026-11-18 14:42:35 UTC through 14:47:37 UTC. The solution Release Rebuild returned zero with 55 test compilation warnings and no errors; the two external playback projects also built successfully.

| Executed scope | Passed | Evidence |
| --- | ---: | --- |
| Ten managed suites | 688/688 | Diagnostics 22, Performance 16, Loader 23, UI/Input 79, Core 24, Scope 30, RuntimeConfiguration 6, Module 40, Hooks 210, Gameplay 238 |
| Diagnostics export | 26/26 | New queue and flush-control counters retained; local directory excluded |
| Native playback modes | 25, 1608 checks | All 17 channel-operation modes, three primary modes and five successor/cleanup modes |
| Owned-call registration | 29/29 | Each registered Gameplay owned-call check printed exactly one actual PASS line |

Source manifests before and after execution have SHA-256 `e6aa40557a75efb0b735f8711b48fee68d8b016d09e952ddc0c28bd08b46df7c`. Assembly manifests both have SHA-256 `b294b727f9acec9f04a3ba4b4da822a4b61570fc5e4801b6a0c5784c760955ea`. The run summary SHA-256 is `91e308f833e6dfa9772cf5d79a93825af29d96e0d42d6af073f19bead24e042b`. The user's README hash remains `cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364`.

## Behavior covered

Standard operations authenticate their actual retained channels and current source/Clock state without repeating full installation and image audits. Explicit installation and inspection still perform those audits. An actual occupied GetTime original refuses rate admission before DSP, pitch or media sampling, without withdrawing a healthy policy or releasing its roots. After the original drains, a new owner operation can proceed.

The new `operation-publication-inflight` case executes the real normal pitch writer, stores pitch 2 and advances its native fence, then starts a same-player GetTime worker with a foreign nonzero method. The native original remains blocked until the queued SDK command becomes nonretryable Unknown with publication rejection 8. Actual V1/112-byte Single stats and the normal writer's physical completion are recorded. The original seven handle identities remain retained; the old policy stays withdrawn, no retry or restoration occurs, and original entry/call/exit are exactly once after release. Physical drain alone does not clear Unknown or release its source responsibility. This case passed 81 directed checks.

Background logs now use one consumer with bounded event and flush-control rings. Explicit cutoffs remain fixed, each flush reports its actual attempt result, and later success cannot erase an earlier failure. A timed-out Dispose leaves busy storage with the consumer until actual completion. Host persistence keeps one running and one latest detached copy; it performs JSON, percentile sorting and process-resource collection in the background. Capture and storage faults preserve normal host phases, and terminal capture avoids disposed providers. Standard input skips detailed comparison and Debug construction. Actual control-phase timing remains observational and cannot interrupt or change native execution, restoration or release.

## Failed executions retained

The earlier operation-inflight refusal, missing namespace build and all later diagnostic/Loader failures remain in their original run directories. The r3 diagnostic cleanup failure hid a parallel-drain timeout. Test areas now confirm actual Completion before deleting their files. r4 exposed that timeout; a diagnostic-only same-source probe then passed 22/22. The original thousand-record and strict sequence requirements remain, with four concurrent batches of 250 and a finite drain per batch as already allowed by the plan.

The r6 run passed Diagnostics and Performance but failed a Loader terminal assertion that treated the first diagnostic capability as the fixture capability. Bounded diagnostic selection follows registry traversal, so the test now selects `fixture.snapshot` by ID and additionally verifies the disposed-provider reason. A Loader-only probe passed 23/23 before the complete r8 execution. No product counters, expected outcomes or pressure checks were weakened.

## Remaining acceptance

The owned group does not cover formal Fresh SourceOperationScope, native watch/SuppressObserver composition or a positive fixed real-game consumer profile. The formal two-run fixture is still a controlled rate host. Source fresh admission remains a preflight rather than a lock held across the write; native publication independently checks actual in-flight state.

Current-package pause/rate/restoration and run handoff, actual Speed/Seek, the comparable 60-second same-machine cost baseline and the three R08 game scenarios remain pending. Next offline work separates ordinary song/score, theme and quick-panel/WAV plugins from development proof, adds schema-3 installation/recovery coverage, and binds acceptance records to the current Runtime payload. Historical packages cannot validate those new files.
