# Reviewed parent/local endpoint writer integration

This directory contains **preparation only**. No payload has been staged or
applied, and no build or test is authorized by the files here. Root should use
the scripts after the candidate metadata repair and its final static review.

## Exact target set

`stage.py` accepts the eight candidate production `.cs` files and two test
`.cs` files. It omits the three candidate production files already byte equal
to main. The expected payload is exactly these eight main targets:

1. `src/RizToolbox.Runtime/NativeRuntime.LineEndpointParentMetadata.cs` (new)
2. `src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionNativeProfile.cs`
3. `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs`
4. `src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs`
5. `src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs`
6. `tests/RizToolbox.Judgements.Tests/CanvasPositions/FakeNative.CanvasPositions.cs`
7. `tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs`
8. `tests/RizToolbox.Judgements.Tests/Program.cs`

The test bodies remain candidate bytes; stage changes only the registry label
prefixes `actual endpoint writer ` and `actual parent writer ` to `Line ring
source endpoint writer ` and `Line ring source parent writer `. This preserves
the 300-case legacy exclusion. `Program.cs` gains only
`--line-ring-endpoint-writer-case N`, with a dynamic range from the registry;
the existing `--line-ring-endpoint-writer-checks` group selector remains.
`scripts/Build.ps1` already dispatches the group selector and remains at SHA
`19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b`.

## Handoff to root

1. Finish the candidate metadata repair and review the final actual candidate
   bytes. `python work/line-endpoint-parent-integration-20261113/stage.py --inventory`
   is read only. It prints the final candidate snapshot SHA, case count, and
   eight target paths. If the candidate changed again, repeat review.
2. Root writes a receipt JSON with `readyForMainIntegration: true`,
   `reviewedBy: "root"`, and `candidateSnapshotSha256` equal to the inventory
   output. Root may add a review note or review evidence hash. Compute the
   receipt's SHA-256 and pass it to `stage.py --review-receipt PATH
   --review-sha256 SHA`. Stage snapshots payload bytes under `files/`, exact
   rollback bytes under `before/`, and writes `payload-manifest.json`. It
   does not mutate main.
3. Review `payload-manifest.json`, the eight staged files and the diff against
   `before/`. Compute the manifest SHA. Apply with `python
   work/line-endpoint-parent-integration-20261113/apply.py
   --review-sha256 RECEIPT_SHA --manifest-sha256 MANIFEST_SHA`. Apply requires
   the exact accepted 891-input graph and both hashes, performs conditional
   atomic replacement, verifies the resulting 892-input graph, and records the
   application. On a failure it restores only bytes it can still identify.
4. Launch `validate_main.py` as the original controller, then launch
   `observe.py --script-sha256 VALIDATE_SCRIPT_SHA` while it is live. The
   observer holds the original Windows process handle through exit and writes
   `controller-handle-exit.json` into the fresh validation output directory.

The fresh validation output is
`D:/riz-build-cache-next/line-endpoint-parent-integration-20261113/main-r1`.
It refuses to reuse an existing run. It builds the six production Release
projects with zero warnings and errors, then the solution with zero errors,
using fresh output bytes. It checks six production pairs, two test pairs,
12 dependency files, the three pinned native fixtures, exact input and
control hashes, the old 300 names/order without executing them, and exact
selector usage/exit 2 for invalid name and arity. Every final writer case
(currently 29, with more expected from repair) runs in its **own process**,
with at most two children at once. Target provider 6, chart occurrence 5,
endpoint local 6, numeric 13, Ring X 7, selected source
13/14/17/18/19, basis 1, and Runtime ABI 51 are also exercised. Each
test child has a 30-minute limit and a 512 MiB available-commit floor. No
fresh CLI home or permanent environment change is used.

For final writer count **N**, expected main compiler inputs are **892**,
default registry cases are **389 − 17 + N**, focused passes are **N + 94**,
and natural steps are **N + 17**. This is scoped offline evidence for actual
`SetParent(target, false)` plus local endpoint writes. The journal remains
observational; it is not an atomic compare-and-exchange or complete Seek.
