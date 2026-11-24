# 本地播放的判定与计分输入来源

2026-10-27。只读分析现有固定构建，供正式本地播放接线使用。没有启动游戏或执行游戏函数，没有修改生产准入；此页不是播放成功证据。

[实际反汇编原件](D:/riz-build-cache-next/local-playback-source-audit-r1-20261027/static-inspection.json) SHA256：`61014291b9d38ad7c99429f379ad7cdf1900cb832175c901e72930c1afdf8f44`。分析前实际完整读取并验证 GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；工具为 pefile、Capstone，复跑脚本在同目录 `inspect-sources.py`。IDA 启动脚本实际返回缺少 idalib-mcp，未假称已使用 IDA。

## 实际判定选择分支

固定 `NormalMode.LoadWorkshopLevel`（RVA `0x3c10d0`）的实际字节显示：

| 实际位置 | 行为 |
|---|---|
| `0x3c1163`、`0x3c117b` | 读取 `WorkshopChartMetadata` 的 difficulty（`+0x20`） |
| `0x3c1186`、`0x3c11af` | difficulty 为 0 时选列表下标 0 |
| `0x3c11b3`、`0x3c11de` | difficulty 为 1 时选列表下标 1 |
| `0x3c1207` | 其它 difficulty 选列表下标 2；新本地格式可收紧为只接受 0..2 |
| `0x3c1218`、`0x3c121b` | 从静态列表取对应 `JudgeData`，调用实际列表 getter |
| `0x3c123b`、`0x3c1251` | 将所选判定对象作为第五个业务参数传入 `InsideLevelParams` 构造函数 |

既有 dump 将相应静态成员标为 `InLevelSettings.JudgeDataList`；该名字是元数据定位线索，实际适配仍必须通过 NativeRuntime 验证声明类型、静态性、列表元素类型及方法签名，不能把符号名称当实际对象证明。不是按 `SettingsControl.DetailJudgeIndex` 选择判定窗口。

`JudgeData` 的四个 float 为 `judgeTime`、`normalPerfectJudgeTimeInChallengeTime`、`maxPerfectJudgeTimeInChallengeTime`、`badTime`。现有 bundle factory 已核对其类型、有限正值并创建独立本局副本。新来源应保留真实列表/元素根并二次核对身份，不应借用已结束的旧局 controller 或创建全零默认判定。

## 计分元数据与经理来源

原 `WorkshopChartMetadata` 明确包含 difficulty、level、maxHit、maxScore；后两项是内容元数据。现有 SDK 本地谱面请求仅有 Id/Name/Content，所以缺少提供者明确声明计分信息的入口。可通过向后兼容的可选元数据属性补齐，逐修订复制、校验范围与完整性；不能将探针的 `0/0` 哨兵带入真实 controller。作者声明的数值应明确标为本地内容来源，不能冒称独立计算或官方成绩。

现有原生 `LocalNativeBundleProbe` 已通过 `GameManager.Instance → inLevelControl` 定位实际经理与控制器管理对象，并在根保留后稳定二读。正式来源可以复用同样的字段/类/Unity 活性检查；它不需要先启动一局官方谱面才能取得 `InLevelSettings` 的判定列表。

## 限制

原件按下一条 dump 方法标签截取反汇编，完整机器控制流与泛型身份不能仅靠标签证明。共享代码地址列出全部符号别名，不将某个别名当唯一实际调用者。上述静态分支解决输入选择设计，不能授予生产首次写入许可，也不替代同进程 native 类型/对象验证、来源稳定性或真实游戏播放验收。
