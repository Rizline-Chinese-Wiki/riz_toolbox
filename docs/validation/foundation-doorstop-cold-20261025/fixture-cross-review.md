# 冷启动验证器与父进程交叉审查

审查时间：2026-10-25。范围是只读审查本批 `verify-cold.py`、`scripts/Test-WinHttpColdFixture.ps1` 及 root 已产生的 r3 原始证据；没有构建、启动目标进程或操作游戏。审查者参与编写过冷启动 fixture，未编写上述验证器和父进程脚本，因此这是交叉检查，不是全部实现的独立审计。文档类型为普通工程审查，`flavor = null`。

当前五个冷启动 case 的记录支持其限定结论：三个正常进程完成真实静态依赖链观察，两个负例按显式身份拒绝路径退出；没有把 SEH、超时或异常进程退出当成负例通过。本文没有观察 root 整批构建命令的最终终态，不能替代该终态及后续归档。

## 证据

| 证据 | 位置 | SHA256 |
|---|---|---|
| E-CR-1 验证器 | [verify-cold.py](verify-cold.py) | `384cb349b33e8e68dc30c51732fb2fe6602842b09d68b1fa0a52631c960abdbf` |
| E-CR-2 父进程脚本 | [Test-WinHttpColdFixture.ps1](../../../scripts/Test-WinHttpColdFixture.ps1) | `5b8d20343dda70679d767a03f545e324b2ceaee260666733a4de3ed76a35f492` |
| E-CR-3 已完成冷启动父进程结果 | [fixture-result.json](C:/Users/71957/riz-build-cache/doorstop-cold-r3-20261025/cases/fixture-result.json) | `47f1dc9abd32e03a630a3d0e48491ccd7e5b15438456505dc5ad1a7c7aecfa5d` |
| E-CR-4 确切二进制契约 | [pe-proof.json](C:/Users/71957/riz-build-cache/doorstop-cold-r3-20261025/cases/pe-proof.json) | `4112fe8e61bfbb9e78517519065c20f042f6d80da35f22b6ece736b0ff87042f` |

E-CR-3 的 `passed=true`、`verifiedCases=expectedCases=5`、`allInputHashesUnchanged=true`。逐一只读对照 `process.json`、`result.json` 和 `case.json`：

| case | 实际 PID | 实际退出码 | 核验断言 | API 行 / 对照对 | SEH |
|---|---:|---:|---:|---:|---:|
| normal-1 | 4576 | 0 | 1712 | 144 / 66 | 0 |
| normal-2 | 13588 | 0 | 1712 | 144 / 66 | 0 |
| normal-3 | 43968 | 0 | 1712 | 144 / 66 | 0 |
| missing-proxy | 45848 | 1 | 52 | 0 / 0 | 0 |
| legacy-proxy | 57436 | 1 | 52 | 0 / 0 | 0 |

这些结果属于 root 的执行，本次没有重复运行验证器或目标。可离线复核时读取上述现有 JSON 和同目录原始 `.bin`，或由 root 使用原测试入口创建新证据目录；本次只读检查不产生新的运行通过次数。

## 发现与证明范围

**F-CR-1，已验证：正常与拒绝路径有独立进程边界。** 依据 E-CR-1、E-CR-2、E-CR-3。父进程使用确切宿主绝对路径，保存实际 PID、退出码和超时状态，正常要求 0、身份拒绝要求 1，超时直接失败。验证器另核实际 PID、`sehCode==0`、精确错误消息及失败形状。两个负例要求五阶段私有 API 地址和 Info/entry 记录全零、零 API 行、零比较对；不能用宿主捕获到异常后的普通退出码冒充身份拒绝。

**F-CR-2，已验证：原始传输、调用语义与私密输出核验覆盖实质数据。** 依据 E-CR-1、E-CR-4。16832 字节 importer、2592 字节 phase、320 字节 call 的精确 EOF、SHA、字段和 JSON 均核对；全局序号必须恰为 `1..N`，importer 必须占前 12 行。三工作线程各 40 行，线程 ID 和路由独立核对；Open 参数、第五参数、完整 BOOL 返回值、LastError、真实 session 对应释放、每个私密字符串的有无/长度/盐化摘要以及 GlobalFree 计数均核对。没有保存 proxy/PAC 明文，也没有网络追踪证据；固定无 PAC/WPAD 的输入及源代码不等于证明整个系统内部从未发生网络活动。

**F-CR-3，已验证：正常结果绑定确切生产代理和已完成 DllEntry 记录。** 依据 E-CR-1、E-CR-4。PE 证明逐项绑定 82 个名字与 ordinal、真实 entry、四个尾跳转桩、MAP 私有 RVA、生成头和整文件 SHA；普通导入边必须是 Host→Importer→winhttp.dll，Host 不得提前导入 WinHTTP，双方不导入 Initialize/Attach。正常运行的模块绝对路径/file ID 对照已持有 PE；每阶段 Info、完成的 1304 字节 DllEntry 记录和之后三个观察点必须相同，且初始化/发布次数为 1。额外引用要求回到同一个模块基址，不能把一次额外 LoadLibrary 引用写成重新初始化测试。该证据支持本机确切构建的链路；不是所有加载器环境的普遍安全证明。

**F-CR-4，证明边界：两个负例不确定实际拒绝模块的路径。** 依据 E-CR-1 第 294–313 行和 E-CR-2。父进程确实复制并持有旧代理文件，missing 部署也未复制代理；但拒绝快照只记录 `rejectedProxyBase`，未记录被拒绝模块的实际路径/file ID/SHA。因此可表述为“在缺少本地候选 / 部署旧代理条件下干净拒绝”，不能据此直接断言前者一定实际加载 System32、后者一定实际映射了指定旧文件。现有文件的只读共享句柄会拒绝写入/删除；目录中不存在的 `winhttp.dll` 没有同类句柄锁，故缺失状态本身不是被锁定的文件身份。需要精确证明拒绝模块身份时，应另记录安全公开模块身份后再执行私有 RVA 拒绝，不应扩大当前结论。

**F-CR-5，可加强的验证器约束：初始化阶段的整体顺序未直接断言。** 依据 E-CR-1 第 353–367 行。验证器按 phase 分组检查前四阶段各为 Close(NULL)/Proxy(NULL)，没有直接要求前八行 phase 序列为 `[4,4,5,5,6,6,7,7]`。本次只读查看三个正常 case 实际都为此序列，随后是四个 phase 11 调用，故没有实际顺序异常；可以在未来版本把此已有证据变成明确自动约束，保留本批冻结来源。

**F-CR-6，证明边界：CFG 与 C/C++/TLS 证据。** 依据 E-CR-1、E-CR-4 及已冻结 fixture 源码。验证器核真实 CFG load-config、instrumentation 标志和有序可执行 GFID 表，且实际受检调用已完成；它未查询进程运行时 CFG policy，因此结论应为“保留 CFG 编译/链接结构且这些实际调用成功”。TLS 核有实际 callback；C/C++ 阶段还依赖持有源码中的外部 C linkage、`.CRT$XIC/.CRT$XCU` 和 `/INCLUDE` 保留以及真实阶段记录，验证器未另从 MAP 还原每个 C/C++ 槽对应函数。不能将这些证据扩大为编译器、所有 CFG 配置或任意 TLS 环境的普遍证明。

## 路径与结论

P-CR-1：E-CR-1/2/3/4 → F-CR-1/2/3 → 可使用本批确切静态冷启动结果，继续生产启动链接入工作。

P-CR-2：E-CR-1/2/4 → F-CR-4/5/6 → 报告保留拒绝模块身份、顺序自动约束和运行时 CFG policy 的证明边界；不把它们写成 V4、真实游戏或整个加载器安全已完成。

本次未发现需要丢弃五个已记录 case 的异常证据。没有修改 626 个冻结源文件；只新增此审查文档。
