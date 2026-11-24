# Start 内联清理与真实回执阶段报告

2026-10-26。普通逆向及实现验证，flavor = null。[授权与范围](validation/foundation-engine-inline-20261026/scope.md)。

本批已把 Start 内部清理分支的原生观察接到 C# 实际节点账本，并通过自有进程联合验证。完整 PC Mod 底层尚未完成；生产接入、取消、scheduler/wait、全消费者退休和真实游戏验收仍待完成。游戏实测继续按用户要求暂缓。

## 结果及适用范围

| 验证层 | 实际结果 | 适用范围 |
|---|---|---|
| 固定 UnityPlayer 文件 | 三处指令及完整 Start 摘要、跳转和展开信息吻合 | 固定版本离线事实，无游戏执行 |
| 独立 Kernel | C++/MASM 严格构建成功；13032 条磁盘核验通过 | 旧 ABI2/112/32 保留，新增侧 ABI1/144；15 导出、7 导入，151 条 runtime function，无入口/TLS/实际 CRT 依赖 |
| 原生测试 | 15 个独立自有进程，7137 条断言通过 | 状态保留、真实内存释放后 End、缓存 tuple、嵌套/复用与异常拒绝 |
| Runtime | 构建 0 警告/0 错误；15/15 协调器、10/10 对象身份组 | 含新增 9 组订阅寿命回归；不是真实 Unity 对象测试 |
| Audio/content | 完整 718/718；构建 0 错误、17 条夹具警告 | 含新增 20 组内联回执和账本测试；这不是全仓库测试总数 |
| 原生到 CLR 联合 | 四个独立进程全部退出 0，189 条托管、293 条原生断言通过 | 真实自有 DLL→Kernel→CLR callback→bridge/dispatcher/coordinator→实际身份/节点/内联账本，tracker provider 仍受控 |

## 证据

以下观察日期均为 2026-10-26，来源为本机命令及原始日志；命令需要现有 Windows x64、VS 2019 Build Tools、.NET 8、PowerShell 7、Python/pefile/capstone。源与产物归档用于保留本轮事实，不宣称包含全部传递依赖或可在任意新电脑直接重现。

| ID | 原件 source_ref / artifact_path | content_hash SHA256 | 观察 |
|---|---|---|---|
| E-01 | [fixed-sites.json](C:/Users/71957/riz-build-cache/engine-inline-contract-r1-20261026/fixed-sites.json) | `89657c7b8bbdfec2dc0a1cfba847b74e09fd35c2d8c8b1c118fd69c0caf5c1ee` | Begin RVA 7924CD，EndFree 79257D，EndRetained 79259D；后两者均跳往 79299E |
| E-02 | [inline-pe-verification-r3.json](C:/Users/71957/riz-build-cache/engine-inline-kernel-r1-20261026/inline-pe-verification-r3.json) | `237754bda30ff2a764a0a2ab70412a1cefba7e359c5346b233b1f7c2a5d1ba01` | passed=true，13032 检查；Kernel DLL SHA 为 a0e01ed1619b6c6250665aa29abdd218f2dc8fcdb7283ca3411ce2384d959884 |
| E-03 | [原生 15 场景结果](C:/Users/71957/riz-build-cache/engine-inline-cases-r1-20261026/fixture-result.json) | `fd98dab5e7621a7c90172e1733de9bb97a9989f56bb958d3566ead6be707dff4` | 15 项 passed=true，均独立 PID/exit0，7137 断言；每项有原始二进制回执 |
| E-04 | [Runtime 结果](C:/Users/71957/riz-build-cache/engine-inline-runtime-r1-20261026/test-result.json) | `27ab8b07f7c1bba9391a3c9f5886a3f34cad41119cc2f12c71b214a7a8b192b8` | 15/15+10/10；315 声明源摘要前后不变 |
| E-05 | [Audio/content r2 结果](C:/Users/71957/riz-build-cache/engine-inline-audio-r2-20261026/test-result.json) | `da8ad27fcd24e6619309f7880dde4f5b8d0f542fb69467a268bb05e451f3ca7f` | 718/718；450 声明源摘要前后不变，17 条夹具警告保留 |
| E-06 | [Native→CLR 四场景结果](C:/Users/71957/riz-build-cache/engine-inline-managed-cases-r1-20261026/fixture-result.json) | `c7eab4d2ccd00416e4f227148f738c29fc32794864c2cfc835071e0caed6a4a3` | PID 35132/51516/50260/46712；free/retained/nested/address-reuse 均 exit0、Outstanding0、Pending=true |
| E-07 | [本批汇总索引](C:/Users/71957/riz-build-cache/engine-inline-evidence-r1-20261026/evidence-index.json) | `dedcef7edc422228630f9ce1c12fee13451ac5e253543bd1bec33d1082d4f837` | 17 项 native/runner 源、179 个实际产物/日志/回执及各场景的摘要复核；整体完成和生产许可均 false |

E-07 还引用三份归档：17 项原生及核验源码 ZIP SHA `b838de62f9330576923cbd79dd0b8b73ff039a264d468cd04f6e8b6f8682c0cd`；454 项托管及构建输入 ZIP SHA `bb83de5ce11960bf4664da7482c22e5d5c2a72861f0aedb39565d3c0ef845781`；179 项实际二进制、对象文件、日志和回执 ZIP SHA `e21048cb881cd38f97d92343c257ec74e5de8c2f5ed0abf1bdd94b7437e643e3`。归档和原件均在本机，未上传。

## 发现与对应实现

| Finding | Evidence | 实现含义 |
|---|---|---|
| F-01：不能由空输出或 AL 单独确认内联清理 | E-01、E-03、E-05、E-06 | 增 Begin→EndFree/EndRetained→StartExited；核同一个实际 Start、完整 tuple 和三处 AL 一致 |
| F-02：End 不能读取可能已释放节点 | E-03、E-06 | Begin 缓存 node/owner/handle/cache/type/ref/outputslot；End 只使用缓存与现场 RBX/R12，真实 free 后仅用 VirtualQuery 查页面状态 |
| F-03：回调中卸载不能放弃旧调用 | E-04、E-05 | typed Enter 之前冻结 admitted 成员；side 只能匹配私有 Start binding；旧 Start 原函数内子调用保留，真实 Complete/Dispose 后再撤观察绑定 |
| F-04：侧回执不能越过原 Start 的结束屏障 | E-05、E-06 | sideExit 先于主 completion；主 Start 实际 Complete/Dispose 且没有 Execute/OpenCalls，才确认该代 FinalCleanup |
| F-05：联合链已通，但生产及所有消费者结束未证实 | E-02、E-06、E-07 | Configured 自有桥不提升为 production owner；EndRetained 不宣称原生分配已 free，两个 endpoint 都不签取消或未来生产者关闭 |

节点关联只接受同步 Execute 已建立的实际 node generation，不凭侧回执制造节点。Start-only 父序列和深度由实际物理调用链核验，foreign Start 遮住旧 ancestor；OS thread ID 与 CLR managed thread ID 分别检查。空输出、refcount 或一份裸 frame 都无法创建私有完成凭据。

## 复现命令

以下在当前源码目录和已有工具链运行，创建新的临时输出，不覆盖本批证据，也不触碰游戏。它重现自有原生及联合正向场景；完整 Audio/content 另需既有三个初始化原生夹具，原路径与摘要记录在 E-05。

```powershell
Set-Location 'C:/Users/71957/Desktop/riz_toolbox'
$taskRepro = Join-Path $env:TEMP ('RizInline-' + [Guid]::NewGuid().ToString('N'))
$taskKernel = Join-Path $taskRepro 'kernel'
$taskNative = Join-Path $taskRepro 'native'
$taskJoint = Join-Path $taskRepro 'joint-native'
$taskManaged = Join-Path $taskRepro 'managed'
& ./scripts/Build-EngineNodeKernel.ps1 -OutputDirectory $taskKernel
& ./scripts/Build-EngineInlineFixture.ps1 -KernelDirectory $taskKernel -OutputDirectory $taskNative
& ./scripts/Test-EngineInlineFixture.ps1 -BinaryDirectory $taskNative -OutputDirectory (Join-Path $taskRepro 'native-cases')
& ./scripts/Build-EngineInlineManagedFixture.ps1 -KernelDirectory $taskKernel -OutputDirectory $taskJoint
dotnet build ./tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:RizEngineProductionProfileSource='C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs' -o $taskManaged
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
& ./scripts/Test-EngineInlineManagedFixture.ps1 -BinaryDirectory $taskJoint -ManagedDirectory $taskManaged -OutputDirectory (Join-Path $taskRepro 'joint-cases')
& 'C:/Python314/python.exe' ./docs/validation/foundation-engine-inline-20261026/verify-inline-kernel.py --directory $taskKernel --output (Join-Path $taskRepro 'kernel-verification.json')
```

r5 的生成指纹仅供受控测试构建使用，其旧 Kernel 哈希不描述本批新 Kernel，也不授予生产许可。重现时每条 PowerShell 构建脚本失败会抛异常；不要把后续命令的成功覆盖前项失败。

## 失败修正与已知限制

原生夹具首个构建因有符号比较警告被 /WX 拒绝；修正类型后 r2 可构建，r3 加入完整 LastError 哨兵。PE 核验器先错误地把被 /NODEFAULTLIB 禁用的 COFF libcpmt 提示视为依赖，再错误要求 MASM SET_FPREG 的原始 OpInfo 为 0；两项检查已按实际链接产物和展开编码纠正。全部失败日志和后续成功结果保留；没有为通过核验而改变原生实现。

Audio r1 的 717/718 是新测试错误：它先用失效 receipt 把账本置 Unknown，再要求账本接纳一个新节点。修复为先观察节点再拒绝过期 receipt，验证原节点保持未完成；生产校验未放宽。失败源码与日志保留。

联合夹具的 provider 是受控 receiver/Enumerator 标签，没有运行真实 Unity metadata、GC、tracker backend 或 factory 根释放。地址复用的两个 work 使用独立 tracker 身份，不能宣称已联合验证同 tracker slot 的所有代次复用。机器 CET 是否实际启用另有回执，不能从 PE CET-compatible 标记推定。

两个资源保留边界仍未完成：退休订阅在主 Complete/Dispose 回调内重入时还缺独立 closing 身份支持；异常 side Exit 的 logical context 可能永久保留 Outstanding。当前代码保守保留 Unknown/Pending，不把它们伪装为清理完成。

## 后续 Path

| Path | 依据 | 必须补齐的条件 |
|---|---|---|
| P-01：生产侧发表 | F-01、F-05 | 真实启动窗口、同 loaded Kernel/Start 身份、三处固定 site/relay/continuation/RX/CHAININFO 的统一不可变凭据；当前 InlineInfo 未返回 site 配置，旧 V4 不足以补此证明 |
| P-02：调度和等待 | F-04、F-05 | scheduler 登记/消费/删除，wait 发布/撤销/真实调用责任，以及合法迟到路径；不能由本地 inline cleanup 推导未来不会调用 |
| P-03：完整生命周期 | F-02、F-03、F-05 | closing 回调边界、取消完成、全消费者 verifier、冻结来源五根与其他 owner 最终退休，再更新候选和开展游戏验收 |

用户要求的 B01–B19 范围未缩减，验收勾选保持。当前真实进度见[一页说明](../.docs/PROGRESS.md)和[本批记录](validation/foundation-engine-inline-20261026/README.md)。
