# 游戏启动 admission 修正与证据报告

当前最小正规数版本（r2）完成了新 1172 项输入图及辅助 Build 的限定离线验证：六项生产构建、完整解决方案构建、8 项 tick 专项及 Runtime383/Module31/GameCamera55 全部自然返回 0。安装器实际 wait 为 0，记录当前六 DLL 与 Examples 等 23 项自有包字节一致、用户配置保留、游戏 GameAssembly SHA 未变。随后 PID15924 的一次游戏启动被 native `operation.prove.block-present` stage145/row14 阻断，Loader 未进入；游戏标题页正常响应并正常退出，没有进行回合。本次 managed 修正仍缺游戏内执行验收，SDK 总目标、B01–B19 全部完成和 CompleteSeek 许可均保持未完成。此报告采用通用技术文档与 Base 要素，`flavor = null`。

范围、授权和排除项见 [scope](validation/game-startup-admission-20261115/case/scope.md)；逐项哈希和完整 E/F/P 字段在归档的 `case/evidence.md`、`case/findings-path.md`。归档子任务没有改 Root C#/Build/native 或控制后台进程。

## 验证范围

| 项目 | r1：v1 次正规数 camera interval | r2：v2 最小正规数 camera interval |
|---|---:|---:|
| 完整冻结解决方案输入 + 辅助 Build | 1172 + 1 | 1172 + 1，新源与新输出 |
| 原始 supervisor/controller | 40656 / 15488 | 34372 / 43772 |
| 原始 wait | 0；10 个自然子步骤 | 0；6 个自然子步骤 |
| 六项生产构建 | 0警告、0错误 | 0警告、0错误 |
| 完整解决方案构建 | 0错误 | 55警告、0错误 |
| tick 专项 | 8 | 8 |
| 普通套件 | Runtime383 / Hosting20 / Adapter10 / Hooks210 / Gameplay175 / Module31 / GameCamera55 | Runtime383 / Module31 / GameCamera55 |
| 普通用例总数 | 884 | 469 |

r1 与 r2 的源只差 `GameCameraModule.cs` 和 `BuiltinManifestContractChecks.cs`。归档保存 r2 全部输入，以及两项 r1 差异文件，可重建两个独立哈希图。r1 的额外四套件属于旧 v1，未借用于认证 v2。8 项专项也纳入 Runtime383，不能再当不同用例重复累加；完整解决方案构建成功也不代表所有测试已执行。两次结果的 `fullManagedMatrix`、`fullJudgementsRerun` 均为 false。预备 manifest 中 `compiled/testsExecuted/executed=false` 是当时冻结的准备状态，实际完成状态以随后独立 result 与原始 wait 为准。

当前 Runtime overlay SHA 为 `c1a0bdf18d9f2d6d0bedabfa887916ae729e6df6475d75f986c8924f7c660e31`；模块 v1/v2 SHA 分别为 `0da87ba51619f474d9c66c761ef0c3b5cf5717047f585be39be4de37ad9c9b78` / `e1f5442457bd8f1aedf044c26ecd838165669756c18540f3c38d2d20466d6ee3`。当前 r2 result SHA 为 `fbb63a9cd13d61b7df8a1625afc7ebaa54b849f568c24168e950ce24f2b39538`，辅助 Build SHA 为 `fbb669b9f2527db0472ef6ec02586ffce99af37016cd164cf40b6981978439f6`。这份辅助 Build 是冻结输入，不能与当下 Root Build 混同。

## 行为与限制

Runtime 的契约有明确改变：无关与重入 native completion 不再执行完整 publication proof，也没有 SDK observation/host dispatch。它们仍先验证 frame/当前线程并读当前 native channel。首次绑定前的固定内部 `RuntimeBridge` 筛选会在完整 proof 前调用缓存的 `il2cpp_method_get_name`、`il2cpp_method_get_class`、`il2cpp_class_get_name`、`il2cpp_class_get_namespace` 委托，只读取方法/类型/命名空间；它不绑定线程或方法，不调用 runtime_invoke/game/host，不创建或附加 producer，不授予 Ready。8 项专项与生产源码对应，涵盖4096个无关调用、重入、每次新proof、proof期间通道变化、非法frame/线程、非法channel、异常边界和当前completion身份。[E-003, E-004, E-013 → F-002]

相关 tick 每次仍执行完整当前 `VerifyReady`，随后重新读取并核对 native target/original/entry/epoch/attached/unknown/in-flight/fault，再交付当前按值复制的 completion。没有 proof cache 或 grace interval；原始 native original/result/exception 路径仍在 native 侧。这是多个观察点的顺序检查，**没有原子并发 publication 保证**。外层 admission 的未处理筛选/proof/callback 异常会进入 sticky unknown 并屏蔽诊断异常；具体 `RuntimeBridge.OnProvenAfterInvoke` 仍把现有 `_tick`/host 异常局部捕获和记录，不能把受控 seam 的抛异常测试泛化为所有已被主机吞掉错误都会使 admission unknown。[E-003, E-004, E-013 → F-002]

模块修正保留统一的 finite、positive interval 校验。camera 从零值改为内部最小正正规 Double `2.2250738585072014e-308`，精确位 `0x0010000000000000`；v1 的 `double.Epsilon` 为次正规数，v2 不依赖次正规数保留。没有声称观测或修改实际 Unity 回调线程的浮点硬件状态。聚合测试调用30个真实内置实例的生产构造器和实际 ModuleManager.Add，检查无 native probe，并通过实际 camera 模块验证最小正规数步长、1e-12、1000/240/60Hz及大时间跳变时每个 advancing tick恰重应用一次。旧零间隔负控29/31恰失败在camera生产注册和camera逐tick检查，只作诊断支持，v2接纳使用新Module31日志。[E-005, E-006, E-007 → F-003]

## 安装与有限启动

`install/result.json` 的实际 installer wait=0、naturalExit=true、cleanCommit=true、verifiedPackageFiles=23。归档含完整23项自有包字节和当前六DLL/PDB、Examples DLL/PDB；managed六项与Examples均由r2新构建产生，native三项沿用已独立验证的 fresh diagnostic build3来源。安装记录核对配置保留、GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` 未改。本归档checker只校验这些记录及自有字节，不重新访问游戏。[E-008, E-009 → F-004]

有限启动快照 SHA 为 `b0295c13d99432c9e7e220c51f5986c765333c493617f5cf724bd37301a26c09`，绑定该安装result。当前PID15924的三条native日志显示 `operation.prove.block-present stage_code=145 result=0 operation_row=14 dobby_result=0`、producer-return=0和原始IL2CPP初始化返回1。该会话Loader日志为空、无Unity tick绑定或host初始化、status未变化；正常标题页响应与正常退出属于父任务观察。**不能把这个native先行阻断归因于managed修正失败，也不能宣称managed启动已修好。** 后续native连续分配/证明修正与其新构建测试在本包范围之外。[E-010, E-011 → F-005]

## 携带与复核

证据目录及ZIP见 [归档说明](validation/game-startup-admission-20261115/README.md)。从该目录执行：

```powershell
python ./check_evidence_archive.py ./game-startup-admission-evidence-r1.zip
```

checker仅读取ZIP，对其中1172+1输入图、r1两项差异、控制器/overlay、日志计数/退出记录、当前输出和23包文件作哈希与关系校验；不解压、不构建、不安装、不执行任何历史脚本或DLL、不访问Root/D盘历史路径/游戏/网络。可用`--expected-sha256`对照独立保存的archive receipt。hash-index覆盖ZIP中除自身外每一成员；外置receipt固定ZIP和index/checker SHA。它证明封存字节和记录关系一致，不制造一次新的构建、测试或游戏验收。

完整调用路径与结论：冻结组合源 → 新r2限定执行[F-001] → 内部tick admission[F-002]与真实模块契约[F-003] → 已完成23项安装[F-004] → 有限启动遭native先行阻断[F-005]，见归档`P-001`。时间线在`case/timeline.md`。此前实际tool chunk由父任务报告，明确标识在`tool-observations.json`；launch 0不是test exit 0，原始进程句柄wait记录独立保留。

## 文档检查

- [x] 通用模板/最小Base、范围与授权、时间线齐全。
- [x] Evidence有artifact/hash/repro字段，Finding引用证据且Path每步可追踪。
- [x] v1与v2、新旧源和普通套件范围分开；无旧测试认证新源。
- [x] 明示预绑定元数据读取、无关invoke契约变化、每次新proof、第二通道身份检查、非原子并发限制及原有host局部catch。
- [x] 当前有限游戏启动阻断有快照和日志，未把SDK/B01–B19/Seek宣称完成。
- [x] 包不包含游戏二进制、用户配置、账户日志、历史备份或全量bin/obj。
