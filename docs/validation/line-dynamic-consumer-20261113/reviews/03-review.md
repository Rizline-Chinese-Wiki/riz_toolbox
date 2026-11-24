# Dynamic consumer v4 static review

**Verdict:** the isolated v4 repair is reviewable and addresses the v3 test
timing defect. This is a source and hash review only; no build or test was run.

Exact frozen inputs (SHA256):

- `candidate-manifest.json`: `2ddcaf6c8e1b5ee193d62efdc8a2a921e4656689db7079d199d91128da46b172`
- `HANDOFF.md`: `aa08943f6b43c1eece5cd3d0950dae51da0d2aedffb5816bd16ccf3513c2480b`
- `NativeSeekLineRingSourceChecks.Dynamic.cs`: `3168cb2497b594302260ca68eec962ae98fe6cc78550dc971732ce9c559bfc1b`

The six production files and `NativeSeekLineRingSourceChecks.cs` registry match
their frozen v3 candidate SHA256 values byte for byte. Only the dynamic test
file changed; the registered six dynamic cases remain indexes 25 through 30.
The v4 manifest still states no renderer writes and no complete Seek closure.

The changed subfixture hooks `AfterCanvasSourceRead` for `scale` on the exact
`GeometryCamera`. The only matching camera scale field read in the inspected
adapter path is `LineRingReader.ReadSceneSample`. `Project` first completes
`endpoints.ReadAppliedWorldObservation()`, then calls
`source.ReadSceneAfterAppliedWorld(world)`; that scene sample reads camera scale
after LineControl speed. The hook therefore changes `GeometryUiRoot.Position`
after the first world receipt and before the later complete world observation.
Changing UI root position changes the endpoints' computed world coordinates
while leaving their selected local values in place. The assertions retain
`Reject(Project)`, `fired`, and zero renderer calls. `drifting.Retain()` is now
before `Reject`, so fixture disposal cannot replace a primary assertion or
observation failure with a cleanup failure; it does not alter `Project`.

The test's rejection does not itself identify the thrown branch. The source
ordering and exact callback receiver support the intended between-observation
drift, and root's fresh r2 shadow run is still required to establish execution.
