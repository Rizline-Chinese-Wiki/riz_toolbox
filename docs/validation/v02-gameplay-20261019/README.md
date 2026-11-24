# Actual gameplay and installed Boolean fix

Captured from the game session started at 2026-10-19 15:21:57 +08:00. The user played one round; the captured session reaches LevelOver at 15:22:48.

## Observed

- Four scene events reached the independent example.events package.
- One finish event reached the package: chart.GreatnessEarth.Axya.0.IN, score 66492, Crashed=true, Skipped=false.
- Three main-menu resource aliases appeared.
- No start event was recorded. Start-to-finish RunId correlation is therefore unverified.
- No Warning/Error lines occurred in this captured session. This does not imply the lifecycle is correct.

runtime-before-fix.log and status-before-fix.json are snapshots, not live files. summary-before-fix.json counts package notifications once; each event is also logged separately by the adapter.

## Source correction

NativeRuntime previously decoded boxed Boolean through Marshal.PtrToStructure<bool>, whose default representation reads four bytes. IL2CPP Boolean occupies one byte. A regression fixture containing 00 FF FF FF reproduces false being decoded as true by the previous code. Inactive objects could consequently pass the active-object filter. The current session did not record raw Boolean bytes or selected controller identities, so this is a confirmed ABI defect and a candidate cause of the missed start, not a proven complete explanation of it.

ReadValue<bool> now reads exactly one byte; both field reads and unboxing use it. Boolean arguments also write one byte. Controller diagnostics log only changes in selected identity, information availability, began/over flags or chart ID.

build-fix.log records the complete successful build and 79 checks: Core 24, PNG 10, Hosting 16, Game 29. The three new checks cover false with nonzero surrounding bytes, single-byte Boolean arguments/reads and unchanged integer/float/pointer layout. The package is artifacts/package. The before-fix gameplay evidence above was captured with the previous DLLs.

After the user exited the game, the fix was installed at 2026-10-19 15:32 +08:00. install-fix.log and install-fix-verification.json record successful installation, all 17 package files matching the package manifest, unchanged original game binaries, and normal settings with runSelfValidation=false. pending-package-manifest.json is the same manifest saved when the package was prepared; it is now installed. Gameplay with the fixed DLLs remains unverified.

## Required follow-up

Reopen the game and play a round with the installed fix. Require exactly one start and one finish with the same RunId and matching chart, no repeated event while the result screen remains open, and no observer errors. Normal clear, retry and early exit remain separate scenarios. F06/F10 must remain open until their required evidence exists.
