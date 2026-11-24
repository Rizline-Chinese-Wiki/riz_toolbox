# 局内相机 X 标量正式接入与整树托管回归

普通技术验证（`flavor = null`）；范围见 [scope.md](scope.md)。

本批把已冻结的相机标量原型接入生产树：`GameCameraModule` 在 `RizlineAdapter` 中注册到共享 `SemanticTargetsModule` 之后，复用同一 `GameAssemblySha256`、`GameLifecycleSnapshot()` 与 `MatchesCurrentGameplayRun`；`RizToolbox.GameCamera.Tests` 已加入 `RizToolbox.sln` 和 `scripts/Build.ps1` 的托管矩阵。相机能力仍为 `experimental` / `modifiesGameplay`。`CameraControl.Update` 每帧覆写 X；本批没有添加相机 hook，也没有证明宿主 tick 的 reapply 晚于相机 Update 或渲染前最后一次写入。真实游戏未启动。

## 变更与源码 hash

| 文件 | SHA256 | 说明 |
|---|---|---|
| `src/RizToolbox.Adapter/RizlineAdapter.cs` | `35af7f6f1176ccec9c0631e17e05bfb19bf115768c0fbfbf7997817f9ab5ed7a` | 在 `GameAppearanceModule` 之后注册 `GameCameraModule` |
| `src/RizToolbox.Adapter/Modules/GameCameraModule.cs` | `a77d4d6015c108469bb17370a77a1fe822ff73d0e5b29d3377633d0b4f673fea` | 生产模块；manifest 声明 `riz.game.camera` |
| `src/RizToolbox.Adapter/GameCamera/GameCameraService.cs` | `7fed7ef9c884033c458021fa0bd7efee5ff413e164e712a0dffc8b5208232ffb` | 标量 service、scope 恢复与共享 float 仲裁 |
| `src/RizToolbox.Adapter/GameCamera/NativeGameCameraBackend.cs` | `107d91ba10d997e9496371a156a06fff785deded5dd4160ccbf309fe7bcc5849` | 真实 native graph/accessor 实现 |
| `src/RizToolbox.Adapter/GameCamera/NativeCameraPositionAccessors.cs` | `030c1195fdf587cbe8265f166f7a6bcf5129daab8b5152a571c40f298b2dff27` | Vector3/field accessor |
| `src/RizToolbox.Sdk/GameCameraContracts.cs` | `8cacbdac9bd1da363640c3b913850030f3bcc88ef3509dacc8fb0b9a8b2ec6b4` | 公共 SDK 合同 |
| `tests/RizToolbox.GameCamera.Tests/RizToolbox.GameCamera.Tests.csproj` | `106d4362065a1a83a4ded8185f0b1b3c3ec9ee13821a8f9d54ae52631f12d7a9` | 相机专项测试项目 |
| `tests/RizToolbox.GameCamera.Tests/Program.cs` | `e8ec4c70c324136b645074f2225e9cbf1a7e532bd821187a0226e442a266b6ad` | 33 项检查入口 |
| `tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs` | `4b740c9b60120abf64c6853247f8c9fd3e44d948323f4186d43ba315e047d8be` | 实际 NativeRuntime 检查 |
| `RizToolbox.sln` | `dfe03678ca5257623df10c536c964b1de2cbf830222eb68278f73befb990e785` | 加入相机测试项目 |
| `scripts/Build.ps1` | `d2e94673fa0f1598479a0e19a0420cc601e4d931811bfc3ab261763dee7b007e` | 托管矩阵加入 `GameCamera` |

## 验证命令与结果

```powershell
dotnet build RizToolbox.sln -c Release -t:Rebuild -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false --nologo -v:minimal
# 0 errors；53 warnings（均为既有测试 fixture / 其他 suite 的 CS0649/CS8602）

dotnet run --project tests/RizToolbox.GameCamera.Tests/RizToolbox.GameCamera.Tests.csproj -c Release --no-build
# Game camera checks: 33/33 passed.

# 整树 27 套托管回归（Core..SingleEvent，含 GameCamera）
# 每套 exit=0；总计 2369/2369，见 managed-matrix-summary.json 与 logs/
```

- 解决方案构建日志：[`logs/solution-build.log`](logs/solution-build.log)
- 相机专项日志：[`logs/GameCamera.log`](logs/GameCamera.log)
- 27 套矩阵汇总：[`managed-matrix-summary.json`](managed-matrix-summary.json)
- 每套原始日志：[`logs/`](logs/)

27 套合计：24+10+20+79+317+10+30+16+14+29+21+24+14+19+32+40+33+210+79+43+97+11+1001+79+17+35+65 = **2369**。原 26 套基线为 2336；本批新增相机 33 项，全部通过。

## Evidence → Finding → Path

| Evidence | source_ref / artifact_path | repro_command / 观察 |
|---|---|---|
| E-001 | `logs/solution-build.log` | `dotnet build RizToolbox.sln -c Release ...`；exit=0，0 errors，53 warnings |
| E-002 | `logs/GameCamera.log` | `dotnet run --project tests/RizToolbox.GameCamera.Tests/... -c Release --no-build`；`Game camera checks: 33/33 passed.` |
| E-003 | `managed-matrix-summary.json`、`logs/*.log` | 27 套逐套 exit=0；2369/2369 |
| E-004 | 上述源码 hash 表 | `GameCameraModule` 已注册；测试项目已入 `.sln` 与 `Build.ps1` |

| Finding | severity / category / status | evidence_ids | confidence / location | 结论 |
|---|---|---|---|---|
| F-001 | `n/a_re` / `other` / `validated` | E-001, E-004 | high；`RizlineAdapter` / `.sln` / `Build.ps1` | 相机模块已进入生产注册与标准托管构建矩阵，解决方案无编译错误 |
| F-002 | `n/a_re` / `other` / `validated` | E-002 | high；`RizToolbox.GameCamera.Tests` | 33 项受控 service/native 检查全部通过 |
| F-003 | `n/a_re` / `other` / `validated` | E-003 | high；27 套托管矩阵 | 原 26 套 2336 项无回归；新增相机后 2369/2369 |
| F-004 | `n/a_re` / `other` / `candidate` | E-002, 原静态证据 | medium；真实宿主/相机/渲染次序 | `CameraControl.Update` 每帧覆写 X；宿主 tick 的 reapply 只是协调机会，不能证明晚于 Update 或成为渲染前最后一次写入。真实可见效果、输入投影和停止恢复未实测 |

P-001（`path_type=callflow`）：`RizlineAdapter` → shared `SemanticTargetsModule` → `GameCameraModule` → `GameCameraService` → 具体 native graph/accessor → shared float channel。剩余风险集中在真实回调/渲染次序、持续可见、输入/玩法影响及停止恢复；这些不在本批离线证据范围内。

## 冻结快照说明

`scripts/Test-FoundationSnapshot.ps1` 的 26 套清单本批**未改**：它运行的是旧 source proof 的冻结源码副本，旧 manifest 不含相机文件；把 `GameCamera` 直接加入会破坏该冻结快照的源码闭包。相机专项已进入 live `Build.ps1` 矩阵和 `.sln`；下一全树 source snapshot 应在包含相机源码后另行切新 proof，再扩展冻结清单。

## 边界

- 未安装、未启动、未操作真实游戏；未加载真实 GameAssembly/Unity。
- 未添加 `CameraControl.Update` hook，也未修改 native 操作/发布。
- `riz.game.camera` 保持 `experimental` / `modifiesGameplay`；root/生产准入仍需独立 run effect 审计。
- 性能优化、长期压力和真实游戏实测按用户安排后置。
