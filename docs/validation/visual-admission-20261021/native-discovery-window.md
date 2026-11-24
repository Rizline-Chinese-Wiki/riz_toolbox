# B12 bounded native discovery window

Date: 2026-10-21. Local source change; no game launch or installation.

## Defect and correction

The former `NativeVisualBackend.Discover` always enumerated native `GetValue(0...)`. Its 4096 check happened after the enumerator had already read the next element, so it could read 4097 raw elements while still never admitting later candidates. Sorting material candidates could only reorder that fixed prefix.

Each backend now owns one `NativeArrayScanCursor` containing an integer position. Each discovery pass obtains and roots a fresh `FindObjectsOfTypeAll` array and reads a window with direct typed `GetValue(index)` calls. It never enumerates and discards a prefix.

- Per pass: no more than `min(array length, requested maximum, 4096)` element calls in the visual backend.
- Array validation still requires a native array, rank 1, and a length from 0 through 50000. Rank and length each require one fixed metadata accessor invocation, separate from the element-call budget.
- The next index advances before each attempted element call, including a failed call. Partial enumeration resumes at the next attempted index.
- Length changes normalize the integer position by the new length; empty and null arrays reset it to zero.
- No array pointer or candidate is kept in the cursor between passes.
- Both the runtime window and the backend's complete discovery enumeration hold array roots. The backend root spans material sorting and every yield, so the service can acquire its own receiver root before the array is released.
- Existing `ArrayItems(array)` callers retain sequential full-array enumeration.
- Candidate material priorities are passed through as `DiscoveryPriority`, including TMP and Translucent priority 100.

This change does not classify a bound target as dead when it is absent from the current window. Bound-target eligibility and physical-lifetime checks remain independent in the visual service.

## Coverage and limits

With stable length/order and a positive budget B, all N indices are visited within `ceil(N / B)` complete passes. The test uses fresh native array identities each pass, 10003 entries, a single eligible-target stand-in at index 10002, and budget 4096. It also verifies that each pass performs exactly 4096 direct element reads and only one rank/length query.

If Unity arbitrarily reorders its array between every call, an index cursor alone cannot promise fairness for each object. A target may also be excluded by normal eligibility checks or by service admission/cleanup constraints. Array allocation by Unity itself is not made incremental by this change.

Additional production-path regressions cover wraparound, shrink/empty/null arrays, failed native element access, early enumerator disposal, exact root release, negative budget, wrong type/rank/length, wrong thread, the 50000-element upper boundary, a one-element array, and legacy full-array enumeration. A pure-cursor test injects the integer boundary without allocating a huge native array.

## Validation checkpoint

Nine new cases are saved in `tests/RizToolbox.Runtime.Tests/ArrayWindowRegression.cs`. Synthetic native exports exercise the actual runtime accessor, argument validation and GC-root paths.

Executed once in the root-authorized serial slot:

```text
dotnet run --project tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj -c Release
Native runtime checks: 57/57 passed.
Exit code: 0
```

All 48 existing cases and the 9 new window cases passed, with no compiler warnings in the output. No full solution build, game launch, installation, or live Unity-array fairness claim was performed by this subtask.
