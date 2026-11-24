# N2 stock 目标解析与固定正文证据

本包的当前[私有共同组联合验证](joint-profile.md)实际 **4 进程 / 84 项全部通过**：从 core-r1 冻结源独立构建，映射固定原版 GameAssembly 副本并真实完成 GetTime/SetPitch 双 Prepare/Commit；两个 code profile 接受同一私有组，ClockPublication 实际 revision `1→2→3`，正文/consumer 改动后的 sticky 撤回也清除 native authority。五个 PE/portable PDB 与 467 个源文档 checksum 全部匹配，build/child exit0，0 warning/error；游戏方法和游戏 DllMain 均为 0。

以下 metadata/静态正文部分为历史 build-r1 的独立 **44/44**，只验证其各自冻结源，本轮未重跑或并入当前 84 项。解析器发行 sealed、private-constructor receipt，保留十个声明和 SetPitch 的两个独立 MethodInfo；代码 profile 固定九个物理 hook 入口与五个只读来源/consumer 正文。IL2CPP metadata 与 binding graph 在执行中明确为 synthetic；当前结论只限离线声明、代码和真实私有安装组证据，不提供生产首次安装资格，也不证明全部 engine writer 来源覆盖。`flavor=null`；[授权范围](scope.md)与[时间线](timeline.md)随包保存。

## 目标与参数合同

| 声明 | RVA | native 参数事实 | 解析后的 ABI |
|---|---|---|---|
| MUGMusicPlayer.GetTime | `3fbb10` | instance self/MI，返回 Single | `InstanceSingleReturn0` |
| MUGMusicPlayer.Pause | `3fbd90` | instance self/MI | `PlaybackVoid0` |
| ReplayScheduled / ResumeScheduled | `3fbe30` / `3fc060` | RCX self，XMM1 原 Single，R8 原 MI | `PlaybackSingle1Void` |
| ProgressControl.Play(Boolean) | `400700` | RCX self，原 Boolean 参数，R8 原 MI | `PlaybackBoolean1Void` |
| AudioSource.Pause | `1511640` | instance source/MI | `PlaybackVoid0` |
| AudioSource.PlayScheduled(Double) | `15116d0` | RCX source，XMM1 原 Double，R8 原 MI；wrapper 执行 maxsd(0,input) | `PlaybackDouble1Void` |
| AudioSource.set_pitch(Single) | `15117c0` | RCX source，XMM1 原 Single，R8 原 MI | `PlaybackPitchSingle1Void` |
| AudioSource.SetPitch(AudioSource,Single) | `15117c0` | static 两显式参数与上一行物理参数槽一致，MI 独立 | `PlaybackPitchSingle1Void` |
| AudioSource.set_clip(AudioClip) | `1511a20` | RCX source，RDX clip，R8 原 MI；独立 prologue/unwind | `PlaybackReference1Void` |

`Progress.Play@400779` 直接调用 `AudioSource.Pause@1511640`，随后 `400781` 清 player origin，绕过 MUGMusicPlayer.Pause；本包同时保留 Progress.Play 与 AudioPause 两个目标。这里只复用既有确切 inline call 证据，没有推测其它 inline 点。

`SetPitch` 两声明是一个物理 hook 入口。receipt 的 `PitchInstance` 和 `PitchStatic` 地址相同而 MethodInfo 必须不同，逐声明核 name、staticness、参数数量/类型/类与返回；原 incoming MI 继续由 native wire 原样转发。未把 property 名称当作唯一 writer 来源。

## 真实解析与代码验证

[`NativeRuntime.PlaybackHooks.cs`](../../../src/RizToolbox.Runtime/NativeRuntime.PlaybackHooks.cs) 的 `ResolvePlaybackStockTargets(module)` 自行通过 assembly/class 与 `InspectDeclaredMethod` / `InspectDeclaredStaticMethod` 解析目标，不接 caller 的 target DTO、地址数组或 metadata record。它核实际 declaring class、non-generic/non-inflated/non-abstract、void/Single return、scalar size/typecode/byref、AudioSource/AudioClip 独立精确 class、完整 MethodInfo2022 exported-field 一致性、implementation flags、generic bits、可执行 image allocation 与确切 RVA。`VerifyCurrent()` 再读 fresh exports 并保持原确切 MethodInfo/flags/entry 身份。

[`NativePlaybackStockCodeProfile.cs`](../../../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockCodeProfile.cs) 固定整个文件 SHA 和 14 段完整正文（包含尾部 padding），将 hook 正文与只读 getter/consumer 分列。SetPitch alias 只有一个 pin。当前共同组只允许实际私有 `PlaybackSourceGroupLease` 为 GetTime/SetPitch 两段已签 patch 提供原正文及持续验证，其余正文仍逐字节固定；没有 caller mask/span 排除口。profile 已观察变化后永久拒绝，恢复字节不复活原 profile。

显式 reader pins 仅为 Progress.Update、Dsp.get_DspTime、AudioSource.GetPitch/get_pitch、get_time 和 get_clip，不宣称 ActualSource 的全部 getter 已逐项建立独立语义 profile。其它 accessor 仍依赖现有 metadata 检查与共同组完整 executable/file/DIR64 seal；固定原版 image 上的共同组实际正向见[联合报告](joint-profile.md)。

set_clip 新证据为 80 字节正文，SHA `72f5e3938703c06d264f6e35bb93c85c0642f7157f1540bf41faf6a70b201e90`，RF `[1511a20,1511a62)`；入口为 `mov [rsp+8],rbx; push rdi; sub rsp,20`，把 source/clip 分别保存到 RDI/RBX，末尾恢复 RCX/RDX 后 tail-jump icall。该目标没有沿用 SetPitch 的六字节搬移或展开证明；本包未安装 set_clip hook，也未定位/调用底层 icall。

## Evidence → Finding → Path

| Evidence | source_ref / content_hash | 可复现范围 |
|---|---|---|
| E-STATIC | 既有 `gameplay-native-values-static-r1-20261030/evidence-r3/functions.json`，SHA `dc5a2dfe3e7df3c01cd1e052ae4ceac96af037505e0b2de920572499e14dcc99` | 已有 Pause/Replay/Resume/Progress.Play/SetPitch/Schedule/GetTime/reader 的 metadata 边界与正文；不重新执行游戏 |
| E-IMPORTS | 既有 `E-imports-exports.json`，SHA `a26796ce231678ffe67c6249aa29551c0b46c570d42578a8cd6c03044af56998`；本包 evidence 内保留等价 JSON | 固定 image imports 摘要 10 DLL、exports 386；沿用静态身份锚点，本包不宣称重新全局 survey |
| E-BODIES | 新 D 目录 `manifest.json` SHA `a638ac64742af1d711b620e0be4630d6d72c3db86e40419564957086af6458a1`；`static-result.json` SHA `8605f35783e3f5d621c4edeeb8669335428aa0d20f6d53e577a8ae526ceffcf5` | 实际 pefile 2024.8.26 / capstone 5.0.6，14 body 与既有 hash 核对；新增 AudioPause/GetClip 只取 hash，set_clip 单 body 新反汇编；无正文 relocation |
| E-SOURCE | build-r1 的 `source-before.json` 与 `source-after.json` 同 SHA `ac85df16dc64d80883450c08dda5aeb46e8a9d02b203c6febdecd1e5f87b3e22`，460 输入 | 编译前后全部 frozen input 一致；执行结束时 live 和 owned 输入也均一致；仅证明这份冻结源 |
| E-TEST | `D:/riz-build-cache-next/playback-stock-metadata-build-r1-20261031/test-result.json` SHA `620f55cde33d65ad97f359e8b6e409c34de550f58d17867b7002be009f217ac2`；test.log SHA `b43b380b6804bffab25379fa0eeedaad1b1fd123e031c5a654cdf5fd97ac893b` | 实际 44/44、build/test exit0、0 warning/error；synthetic 实际 exports + MethodInfo buffer、只读 image mapping，0 game methods/0 DllMain/0 native object allocation |

| Finding | severity / status / confidence | evidence_ids / location / 影响 |
|---|---|---|
| F-METADATA | n/a_re / validated / high | E-SOURCE,E-TEST；Runtime resolver；caller-created target 不能发行该 metadata receipt，真实声明逐项约束；runtime exports 在本包执行中为 synthetic，不冒称游戏 metadata 实测 |
| F-ALIAS | n/a_re / validated / high | E-STATIC,E-BODIES,E-TEST；`15117c0`；两 metadata 声明对应单物理 setter；本包没有宣称 engine 内部 writer 完整覆盖 |
| F-INLINE | n/a_re / validated / high | E-STATIC,E-SOURCE；`400779→1511640`；MUG Pause 入口独自不足以覆盖已知 inline Pause；inline native occurrence 正向另包 |
| F-CLIP | n/a_re / validated / high | E-STATIC,E-BODIES；`1511a20`；准确 reference1 参数/prologue 可供独立 native 实现，不能借用 pitch 搬移证明 |

P-STOCK，`path_type=callflow`：固定 file/import 身份（E-IMPORTS/E-BODIES）→实际 IL2CPP class/declared method/MethodInfo2022（E-SOURCE/F-METADATA）→保留 SetPitch 双 MI 单物理入口与已知 inline Pause（E-STATIC/F-ALIAS/F-INLINE）→共同组私有 lease 验证两个已签 patch，其他 source/readers 全部 pin（E-SOURCE；当前真实联合验证另见 E-JOINT）。残余项为真实游戏运行中的 S1/S2 正向、生产首次写入准入、下层 writer/source 完整性、stock completion/readback/provider 与跨局 handoff。

## 复现与边界

静态复核命令：

```powershell
& 'C:/Python314/python.exe' 'D:/riz-build-cache-next/playback-stock-metadata-r1-20261031/extract-fixed-playback.py'
```

该脚本只读固定 D 盘 copy 和 dump。IDA 当前不可用，未安装/启动 IDA 或 GUI；只新反汇编 set_clip 一段。新映射测试 runner 为 [`NativePlaybackStockMetadataChecks.cs`](../../../tests/RizToolbox.Gameplay.Tests/NativePlaybackStockMetadataChecks.cs)，由 root 注册显式 `--mapped-playback-stock-checks` 模式，不加入默认整树总数。复现冻结编译与测试时使用新的不存在目录：

```powershell
$reproOutput = 'D:/riz-build-cache-next/playback-stock-metadata-repro-' + [guid]::NewGuid().ToString('N')
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File 'D:/riz-build-cache-next/playback-stock-metadata-r1-20261031/run-frozen-checks.ps1' -OutputDirectory $reproOutput
```

44 个实际检查包含十个目标/alias 正向、sealed receipt 合同、22 个 signature/layout 变体拒绝、已发行 metadata 两个再验、module 身份与错线程、完整 profile 正向、14 段正文改动后的 sticky 撤回与卸载拒绝。测试未调用共同组 native producer、原跳板、stock 游戏 entry 或实际 getter；本包不借旧 2465/175/34/427，也不把其它代理的 S1/S2 native 检查加进 44。

metadata build-r1 冻结的三个自有源码 SHA（历史输入；当前 joint-r2 四个输入见[联合报告](joint-profile.md)）：

| 文件 | SHA256 |
|---|---|
| NativeRuntime.PlaybackHooks.cs | `1f2d66b18b790092edd4dc1332342bfcabac6a889ae468340c5ee90850aa9f45` |
| NativePlaybackStockCodeProfile.cs | `aefbf1037d903ea39bc8433ac960de3ded8ff65c54f0243b7b7935798af2fa12` |
| NativePlaybackStockMetadataChecks.cs | `33c6aa29bfbd07fefa7d1554a46257af5bc5e576d6ff25e53aaa693f1c8ce1d8` |
