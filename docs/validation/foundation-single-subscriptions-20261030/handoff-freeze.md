# Single 订阅冻结交接复核

2026-10-30 恢复任务后，先读取已完成的权威终态，再进行只读哈希复核。原冻结 `D:/riz-build-cache-next/single-subscriptions-frozen-r1-20261030` 的 `passed=true` 与 **65/65 managed、8/8 native operation 回调进程、17/17 typed/completion 定向进程**均完整；两个构建 exit 0。没有重跑构建或测试，没有改生产源码，也没有运行游戏或安装包。

本包生产源码保持冻结。交接范围严格采用 [overlay-inputs.json](overlay-inputs.json)：17 个完整 owned 文件和 Retirement 的两个 owned 方法。完整实现与测试边界见 [原专项报告](2026-10-30_implementation-single-subscriptions-report.md)。

新输出为 `D:/riz-build-cache-next/single-subscriptions-fixity-r1-20261030`。原结果及失败历史保持原样。[verify-freeze.py](verify-freeze.py) 于 `2026-10-30T03:50:48.716233Z` 实际 exit 0，**21/21** 检查通过：626 个 frozen 源码、622 个 base 文件、ZIP 每个 entry、两侧的17个 owned完整文件及2方法、固定 helper、56个日志、2个程序集、原 evidence-index 的66个 artifact 字节均一致。原权威结果 SHA 为 `a966aec6cd9a809f9d8941bb91b68207ee441c61f0f25b3e6c04658dfa8462b7`，源码 ZIP SHA 为 `388649d9f4aca29f11ec6da27b80399aca38d744a01b3a90542f973f4588a8c9`。

复核记录：[handoff-fixity-result.json](handoff-fixity-result.json)，SHA `443d9059b1ff5a6d09147a5897084395a62c7fd0ca9eba3614aa94b72cb67159`。独立 [进程检查](handoff-process-check.json) 未见 dotnet 或两个测试 exe，也未终止任何进程。

从仓库根复核，必须使用不存在的新目录：

```powershell
C:/Python314/python.exe docs/validation/foundation-single-subscriptions-20261030/verify-freeze.py --output D:/riz-build-cache-next/single-subscriptions-fixity-replay-20261030
```

脚本只哈希、读 JSON 和检查 ZIP。原报告、索引、日志字节不变，本次补充单独保存。

Evidence → Finding → Path：E-final 为原 test-result（上表原 SHA，原时间`2026-10-30T02:22:33.612587Z`，`passed=true`）；E-fixity 为本次结果（上表新 SHA，时间`2026-10-30T03:50:48.716233Z`，上述命令可重现，21项通过）。F-freeze（category=design，severity=n/a_re，status=validated，confidence=high，evidence_ids=[E-final,E-fixity]）确认当前 owned 源码对应本轮已过结果。P-handoff（path_type=callflow）依次读取原终态（E-final/F-freeze）、核全部相关字节（E-fixity/F-freeze）、保持源码冻结。两证据 source_type=file/log，linked_workitem=Single subscription freeze handoff，supersedes=none。后续修改需要新的冻结和适当验证。

本专项没有未完成的订阅退出修复。Single producer → Delivery/ReadCurrent 正向、生产 backend 真实游戏对象初始化及非空 effects 最终闭合仍由独立工作验证。全 B01–B19 统一源码冻结与集成验收由 root 负责。
