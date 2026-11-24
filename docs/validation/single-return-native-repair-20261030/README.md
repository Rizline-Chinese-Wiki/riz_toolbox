# Single-return native 候选边界修复

2026-10-30，`flavor=null`。独立 prototype 已修复两处真实缺口：clip clamp 掩盖 Single 转换溢出，以及候选算术缺少独立 native SEH 隔离。最终 r4 在同一 helper 上实际通过 **19 个用例、159 项检查**，包含一项仍能舍入为有限 Single 的正向边界。源码、PE/PDB、COFF 与结果已冻结到 `D:/riz-build-cache-next/single-return-freeze-r2-20261030/`。本次没有修改仓库 Runtime/Publisher/Gameplay Clock 代码；正式 V7/83 exports 不变，真实 Speed/Seek 与生产准入继续关闭。

授权及边界见 [scope.md](scope.md)，执行顺序见 [timeline.md](timeline.md)，每个原件的路径与 SHA-256 见 [evidence-index.json](evidence-index.json)。未安装或运行游戏，未操作任何账号。

## 实际行为

旧 native-r1 在 affine 结果仍为有限 double、但转换为 Single 会得到 Infinity 时先 clamp 到 clip，因此返回看似正常的 clip 末尾，而 N2 托管时钟会撤回。最终顺序为：

```cpp
if (!std::isfinite(media)) return Fault(state, RizSingleOverflow);
const auto unboundedSingle = static_cast<float>(media);
if (!FiniteBits(Bits(unboundedSingle))) return Fault(state, RizSingleOverflow);
if (media > policy->clipLength) media = policy->clipLength;
```

这里不能改成 `media > float.MaxValue`：`double.BitIncrement((double)float.MaxValue)` 虽大于精确 float 最大值，仍能舍入为有限 float 最大值。新正向用例证明它在 N2 与 native 都成功返回 clip=1000，authority 仍健康。超过有限转换域的用例则保留原始返回 bits，Unknown 保持粘滞，实际 native revision 2 被私有 receipt 条件撤回，托管 N2 同时进入 Withdrawn revision 3。

`SafeCandidate` 只包住候选计算；原 `Single(self, incoming MethodInfo)` 调用留在该 `__except` 外，实际原异常仍传播。定向 fixture 的 native clock reader 返回前解除 SSE overflow mask，返回后在 Candidate 的 double 乘法触发真正的浮点溢出，没有用合成 `RaiseException` 冒充候选算术。最终 helper 吸收该故障，记录 SourceFault=1/Unknown=1，保留 `0x80000001` 原始 float、一次 original 调用和 native 非易失寄存器；observer 完成后恢复调用方 MXCSR。实际原调用抛出时的异常参数及传播由联合矩阵另行验证。

## Evidence → Finding → Path

| Evidence | 原件与复现 | 完整性 |
|---|---|---|
| E01 | 旧 `single-return-freeze-r1-20261030/` 及 native-r1 的 source-snapshot、manifest、PE proof、12/119 与 4/15 结果 | 已确认原件存在；完整路径/hash 在 index |
| E02 | 最终 native-r4 `native-manifest.json`、`independent-machine-pe-proof.json`、freeze 的 `native-symbol-source-proof.json` | 实际 build exit 0；10 exports；153 unwind 项；Dispatch 原调用恰一次；两对 PE/PDB 身份匹配；两份 C++ 对象的实际编译源码校验和匹配 |
| E03 | `single-return-r4-regression-r1-20261030/{native-fault,joint,ownership}-results.jsonl` 及对应 exit code | 三个进程 exit 0；3/25、12/119、4/15；原始 UTF-16LE/BOM 结果保留 |
| E04 | freeze 的三份 `compiled-source-*-proof.json`、`freeze-manifest.json`、`freeze-integrity-proof.json` | 托管 15 对 PE/PDB 身份；每套 7 个相关生产文档及 fixture 的实际 portable PDB SHA-256；95 项归档校验 |

| Finding | severity / confidence / status | location / evidence |
|---|---|---|
| F01：clip 不再掩盖有限 double 到 Single 的溢出，且同一 N2 撤回 | n/a_re / high / validated-controlled | `SingleReturnContext.cpp::Candidate`；E01/E02/E03 |
| F02：Candidate 的真实 native 算术 SEH 保 raw bits 和 sticky Unknown；原异常仍传播 | n/a_re / high / validated-controlled | `SafeCandidate` / `Dispatch`；E02/E03 |
| F03：严格 Max 比较会误拒有限舍入，r4 已按实际 cast 与托管一致 | n/a_re / high / validated-controlled | `Candidate`；`NativeFaultCases.cs` 正向 ULP 用例；E02/E03 |
| F04：本批仅为受控 prototype，不能授予真实 Speed/Seek 能力 | n/a_re / high / scope-limit | `ProductionAdmission=false` 及 scope；E03/E04 |

P01（`path_type=callflow`）：原 native Single 正常完成 → 固定 DSP/media affine 计算 → finite double → finite 实际 Single cast → clip → 临时 candidate → 最终 authority gate 再验 → 返回候选或 raw。计算故障经 managed Exit fault 通知 → N2 owner Withdraw → 私有 exact publication receipt 条件撤回 native（F01/F02/F03）。

P02（`path_type=callflow`）：精确 original 发生真实异常 → Dispatch finally 通知 Abnormal → owner 撤回 → original 异常连同原参数继续到调用者；不会经过 SafeCandidate 的异常恢复（F02）。

## 构建与冻结

最终 helper 为 `D:/riz-build-cache-next/single-return-managed-native-r4-20261030/RizToolbox.SingleReturn.Prototype.dll`，SHA-256 `90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355`。其源 `SingleReturnContext.cpp` SHA-256 为 `20bc81a06223035bd98877841036dba64a27ecfaf535aea1dbb496108e17fd27`。源码快照在冻结目录 `sources/native/`；测试、机器码证据、实际 native Windows PDB 与 managed portable PDB 位于 `artifacts/` / `evidence/`。

native 使用本机 MSVC 2019 BuildTools，C++17、`/O2 /W4 /WX /EHa /fp:strict /Zi /guard:cf`，构建 exit 0。托管使用 .NET SDK 8.0.418，三个相关构建 exit 0；定向与 joint fixture 各有一个既有的 CS0649 fixture 字段警告，ownership 为 0 warning，均无错误。Python 3.14 + pefile 2024.8.26 用于只读实际 PE/COFF/PDB 核验，没有安装 IDA 或操作 GUI。

native COFF 直接证明两份 C++ translation unit 的编译校验和；无执行行记录的 header/ASM 与构建脚本按 native build source-snapshot manifest 单独归档，不冒充 COFF 行记录。托管源码审计针对七个相关生产文档及 fixture；本报告不是全树源快照，根任务另做完整冻结。

## 复现

使用新且不存在的输出目录，不覆盖现有证据。Windows PowerShell 5.1 可直接调用冻结的 native 构建脚本：

```powershell
$f = 'D:/riz-build-cache-next/single-return-freeze-r2-20261030'
& "$f/sources/native/Build-ManagedPrototype.ps1" -OutputDirectory 'D:/riz-build-cache-next/single-return-review-native-fresh'

# 在最终封印的相同 PE/PDB 上重现原型机器用例；三个进程各自独立加载同一 helper。
$h = "$f/artifacts/native/RizToolbox.SingleReturn.Prototype.dll"
$n = "$f/artifacts/native/OwnedSingleReturn.Fixture.dll"
dotnet "$f/artifacts/managed/fault/RizToolbox.Runtime.Tests.dll" $h $n
dotnet "$f/artifacts/managed/joint/RizToolbox.Runtime.Tests.dll" $h $n
dotnet "$f/artifacts/managed/ownership/RizToolbox.Runtime.Tests.dll" $h $n
```

完整源码与符号验证器在冻结目录的 `pdb-check/` 和 `sources/verification/`；其实际调用路径、版本与证据 hash 在 index。验证器拒绝覆盖已有 proof 输出；重验应另选输出路径。`Freeze-Repair.ps1` 的 PS5 续行解析失败及 r3 verifier 对 UTF-16 的首次读取失败已保留诊断；另存的 `Freeze-Repair-r2.ps1` 和兼容 BOM 的 verifier 成功，不修改原始结果。

## 保留边界

旧 native-r1 的缺陷未被历史绿灯改写；native-r2 是中断目录；r3 是通过旧小矩阵但缺正向 ULP 对照的中间版，三者全部保留。本次未重跑旧 17/328944 矩阵，不把其数字加入 19/159。

独立 prototype 仍有 32 个不可回收 entry slots 与每 slot 64 份不可变 successor policy 上限。尚无生产 detour 安装、固定游戏完整 source/rate provider、实际 schedule occurrence observer、可闻同步输出及完整 audio failure recovery 证明。stock fallback 仅保原调用语义，不能证明修改速率后的同步恢复；真实 Speed/Seek 与生产准入仍关闭。
