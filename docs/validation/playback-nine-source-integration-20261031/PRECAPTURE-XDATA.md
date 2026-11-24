# 安装前异常展开数据的精确反例与修复复验

2026-10-31，`flavor=null`。本页只记录独立自有 fixture 的一次明确反例及同一输入在当前源码的结果，不执行游戏代码，不把机械拒绝证明当完整 stock provider。

旧反例原件是 `D:/riz-build-cache-next/playback-source-precap-xdata-review-r1-20261031/review-result.json`，SHA `3f5dfb128d928b10186fd9ba0595c4ad0de2fe8ee05b2615462ab51ca1ef953e`。在 Resolve/Bind 前仅修改该进程内 SetPitch 的 xdata：`0106020006520230 → 0106020006620230`，随后恢复 PAGE_READONLY。磁盘和 executable 正文均未改，未执行 SEH。旧 core-r1 错误地完成9次 Prepare/9次 Commit，group/GetTime保持 active，正常流程190项通过；该通过数不能证明展开元数据可信。

修复在 `NativePlaybackSourceTrampolineProof.SourceFunctionSeal` 中锁定原 PE 文件，把完整原正文内的所有 RF、UW 和 CHAININFO 与文件逐字节核对，并验证实际 Windows lookup 所属。GetTime 和八 source owner 均在构造预验阶段持有这项证据，之后持续重验；所有 owner 构造完成后才允许任何 Prepare。因此不再先准备某个跳板、随后才检查该成员的原展开数据。

当前复验使用已实际通过的 core-r4，SHA `d96e595c2b8244d176b3b748f72d265212489f2a845791f4ba9f6b2a647707d0`。`playback-source-precap-xdata-review-r2-20261031` 保留旧 Program 的完整 mutation block，仅改拒绝期望和计数/页保护核验；唯一 C# overlay 是测试 Program，727个源文件逐一比对，其余源与冻结 core 相同，四个 native 输入与旧反例相同。实际 build、native/CLR test、PE/PDB/source verifier 均退出0：15项检查、5组 PE/PDB，拒绝点在 source owner 构造，Prepare=0、Commit=0；没有发行 group/GetTime lease，没有在修改后调用原 entry 或 SEH。

初版汇总器把实际 `RizToolbox.Runtime.UnsupportedSignatureException` 的命名空间误写为 `.Runtime.Hooks`，导致汇总阶段返回1，原失败日志和 `review-result.json` 保留。`playback-source-precap-xdata-final-r1-20261031/finalize.py` 只修正汇总期望、重新检查已完成的源/符号/原始输出，没有重编或重跑测试。最终 `review-result.json` 的实际汇总退出0，SHA `da05fb89baa662abc2b641d6e2842bada9a166545b616e6c01ade281ee3a9908`；它明确引用被取代的失败汇总，不能把原 runner 的返回1隐去。

实际执行命令（输出目录已存在，脚本会拒绝覆盖；重现须保留旧目录并使用全新输出副本）：

```powershell
& 'C:/Python314/python.exe' 'D:/riz-build-cache-next/playback-source-precap-xdata-review-r2-20261031/run.py'
& 'C:/Python314/python.exe' 'D:/riz-build-cache-next/playback-source-precap-xdata-final-r1-20261031/finalize.py'
```

Evidence → Finding → Path：E-PRE-OLD 为旧实际误接受，E-PRE-FIX 为当前同一 mutation 的零 Prepare/Commit 拒绝和当前 PE/PDB/source；F-PRE（`n/a_re/high/validated`）说明晚捕获不能签发原始展开事实；P-PRE（`callflow`）为同一进程内 mutation → 固定文件 RF/UW 比较 → source owner 预验拒绝 → 无物理安装。完整语义配对、七根/局身份、连续 writer 与真实运行仍由后续合同约束。
