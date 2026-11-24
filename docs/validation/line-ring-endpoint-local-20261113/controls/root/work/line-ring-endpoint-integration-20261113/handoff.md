# Endpoint-local integration payload

Status: prepared and shadow-verified; **not applied** to `src/`, `tests/`, or `scripts/`. Wait for the active 879 main regression to be accepted before deciding whether to merge. The exact four destination paths, add/replace operations, base hashes, and payload hashes are in `merge-manifest.json`. `payload.patch` is a review/apply candidate and passes `git apply --check` against the current workspace. `before/` holds byte-for-byte copies of the two replacement files.

The production file is byte identical to the frozen `NativeSeekLineEndpointLocalTarget.cs` candidate (`980264063b778394518869c94ec804e07f7b2f81d703d452d3287c6cf3a3a74b`). No production call site, native writer, world target, or complete Seek admission is added. The new test file groups the original **19 assertions into six registry cases**. Its fixture constructs basis/source objects through reflection to check the pure local projection; it does not prove live native initialization, parent hierarchy or cycles, or target provenance. The two same-canvas cases retain the raw `EndLine` reference without treating it as a physical end Transform.

The proposed `Program.cs` edits register the six cases in the default registry, admit and document `--line-ring-endpoint-local-checks`, and route that selector. The existing legacy filter already excludes the new names because they start with `Line ring numeric `. The proposed `Build.ps1` edit runs the selector once and checks its exit code. Existing fixed 13 numeric, 7 Ring X numeric, 19 source, 16 basis, and 300 legacy ranges are unchanged.

## Isolated verification

- `EndpointLocalIntegrationShadow.csproj` compiled the full current Judgements test source plus the payload `Program.cs`, new test file, and exact candidate against the previously built main-r1 DLLs. Output stayed under this work directory. `shadow-build.log`: 0 warnings, 0 errors.
- `shadow-run.log`: endpoint selector **6/6 cases passed**. Static comparison confirms all 19 original assertion messages remain, regrouped; the test source has 19 assertion calls.
- `shadow-numeric.log`: **13/13 passed**. `shadow-ring-x.log`: **7/7 passed**.
- `shadow-listing.json`: **300** unique, ordered legacy cases; zero endpoint-local names.
- `git apply --check` succeeded; no patch was applied.

The shadow run exercises the new registry and test class as an executable. It links a prior compiled Adapter DLL, so the main release build still must compile the new production file after merge. No game binary, native process, UI, account, or network was used.
