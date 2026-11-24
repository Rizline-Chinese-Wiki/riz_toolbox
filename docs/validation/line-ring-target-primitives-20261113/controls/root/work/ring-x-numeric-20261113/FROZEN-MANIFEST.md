# Ring X numeric candidate — frozen handoff

Status: isolated candidate; not merged into main `src` or `tests`, and not an actual `NativeSeekLineRingTargetPlan` or complete Seek result. No game binary, game process, native call, account, network, worker, root, registry, or renderer was used. Existing dynamic buffer/forward-selection code was linked for compilation, not copied or modified. The main numeric 874 matrix and its inputs were left alone.

## Exact supported calculation

The two entry points in `candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs` accept an explicit target input and the selected initialized 101-Single ease row. The caller must independently prove that row and target values belong to its original source. The candidate does not accept an old baseline as target world provenance or infer a canonical group seed.

- Static: `q=f(f(CanvasFloor-StartFloor)/f(EndFloor-StartFloor))`; `e=Clamp01(EaseProgress(q))`; `dx=f(EndX-StartX)`; `mixed=f(f(dx*e)+StartX)`; `x=f(f(mixed*ScreenWidth)+CanvasX)`. `CanvasFloor` and `CanvasX` must come from the selected line's Canvas slot.
- Dynamic: `q=f(f(TargetRingWorldY-StartWorld.Y)/f(EndWorld.Y-StartWorld.Y))`; `e=Clamp01(EaseProgress(q))`; `dx=f(EndWorld.X-StartWorld.X)`; `mixed=f(f(dx*e)+StartWorld.X)`; `x=f(f(f(mixed/CameraScale)/SceneScale)-CameraX)`.
- `EaseProgress` first computes `f(q * 100)`, truncates to Int32 toward zero, uses sample 0 for a negative index and sample 100 for index at least 100, otherwise performs separate Single fraction, sample difference, multiply, and add. `q` is never clamped first. The ring caller then clamps the ease value. The result states `PreserveLocalY` and `LocalZ = -0.5f`; it does not supply a complete local pose.
- Every specified scalar and all vector components must be finite. The numeric support domain requires positive screen width or camera and scene scales as applicable, a nonzero exact Single denominator, finite Single intermediates, and a representable Int32 ease index. It uses no epsilon substitution. The output owns an immutable copy of its scalar input and the complete ease row. Guard, reentry, and complete enumeration follow the existing pure numeric kernel's pattern.

## Freeze basis (SHA-256)

| Read-only input | SHA-256 |
|---|---|
| `src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingNumericMath.cs` | `b9dc33d6d759f539419a75f41b5eac6d05be7bd065304ed0be3b7d22860b98af` |
| `src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasTargetCapture.Lifecycle.cs` | `428019201b328718db9af252235ce0fbdfd57e98f0f5cd6dd79a2f7440238344` |
| `work/line-ring-target-handoff-20261113/implementation-contract.md` | `7cb802db59c0f2ccb6ade61a3476956ccfc970a99c0fcc9cae6620a778345807` |
| `work/seek-hold-geometry-native-20261110/LineControl.SetRing.asm.txt` | `0d96a2b943b0ae03068b2e9f59590fe7106f84d16fc0a0553e18d0109cead4aa` |
| `work/canvas-numeric-basis-20261113/candidate/src/RizToolbox.Adapter/Gameplay/SeekCanvasNumericBasis.cs` | `f99a10e534b9d96edd4ab6fc8bc1bec59a69589fe366ab143d1cbd387301101d` |

## Frozen deliverables (SHA-256)

| File under this directory | SHA-256 |
|---|---|
| `candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs` | `311a4c1af3dd2567e96b8f2e095f13c6fb68334680fc619603264a36c94a5ec5` |
| `candidate/tests/NativeSeekLineRingXNumericChecks.cs` | `55b8a0030e658e2ece1cee357cb231bdea461b9bc0e432fb00e4a415343271d4` |
| `candidate/LineRing.RingX.Numeric.csproj` | `dc4b37787174e7e0a9c0a55b597bd5fb8a0e7117d79dabcbfa94754539967d06` |
| `candidate/tests/LineRing.RingX.Numeric.Tests.csproj` | `72583833d0df7adb5c5442dc924e74af57ef9728ed1c4b9cb907eb85ee160c29` |
| `candidate/AssemblyInfo.cs` | `8f50adca88b758b38dfed3467094ec1fb19b2ae9ba5f419915475c6f0ddccd79` |
| `candidate/tests/Program.cs` | `fb6898dd08fa09d225a2ffa0ceb8db206cc3ba87e821c156b286f55a455b854d` |
| `oracle_fraction.py` | `29c015661f0bf0817c67b06221d4ac4438ddb8a77c3fcd1a04606a613e6bc86e` |
| `single-bit-goldens.json` | `3ce67eb819676dac687488c8ac81b5efdc5c451c52673d7b82b314fad44fe5bb` |
| `NuGet.Config` | `964302159308872b9729e1828a1ba897f30634f7bd751cde11ea8e6fed8cfb97` |
| `run-offline-check.ps1` | `2ed45cfe46b34b8d90ceb86bff8d6ae61255549f41ac203dda7dd9a1377d02b1` |
| `make_integration_patch.py` | `df3357655bef6380546c766429868bc2430ea8be7ea44cafc64813cd268d39fe` |
| `integration.patch` | `68bbc832ca47987e2df0a86f19403bad86837e848465e8b97389460313396764` |

## Actual isolated verification

The SDK-style library and console checks target `net6.0` with a real `ProjectReference`; the installed SDK is 8.0.418, with cached .NET 6 reference packs. `NuGet.Config` clears all feeds, so restore has no network source. The test executable used process-local `DOTNET_ROLL_FORWARD=Major` because the machine has no .NET 6 runtime. Each `dotnet` child ran alone, with the four requested process-local environment values and a process-tree private-commit check that aborts the owned child at 512 MiB. No CLI home was created or changed.

| Action | Actual exit | Peak observed private bytes | Log SHA-256 |
|---|---:|---:|---|
| Offline restore | 0 | 32,317,440 | `logs/restore.log`: `2a5dd8732be144bb3ee0ca3cd3842e94e69c819ec263322b73ff9657313b4fe3` |
| Release build, no restore | 0; 0 warnings, 0 errors | 39,284,736 | `logs/build.log`: `9073760cecd184dbd0d093d4fd4c3e4fe1b467cab48d690b9b64b682741b1b9d` |
| Console check, no build/restore | 0; 7 passed, 0 failed | 34,963,456 | `logs/test.log`: `bdedc1a6ab10a9cc1fd3c9bd9cfbb71d534707d079dddbfa7e49a44c5927997b` |
| `git apply --check integration.patch` | 0 | — | `logs/patch-check.log`: `4e77869f16cd0386cc7fe1ecee76b697699f4add6ce2799ebdbb06c02f8db165` |

The 7 tests cover both branch bit goldens from an independent exact-rational Python oracle, negative progress before clamp, overshoot/undershoot, finite and exact-zero boundaries, all 101 samples and malicious enumerations, immutable copy, guard withdrawal/reentry, signed zero, and Y/Z result semantics.

`integration.patch` adds only the new production class, the new test class, and four small main `Program.cs` test-registration/selector changes. The selector `--line-ring-ring-x-numeric-checks` points explicitly to this class's `Cases`; the case prefix remains excluded from the legacy 300 filter. The patch has not been applied. Main-graph target association, actual target world provenance, group seed, writer, renderer, and complete Seek remain open.
