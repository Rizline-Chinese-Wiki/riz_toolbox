# 诊断与单会话证据导出

当前源码提供有界后台日志、状态副本、入口/宿主/插件回调与实际控制阶段计时，以及单会话证据导出。真实游戏同机成本基线和 B18 压力验收仍待完成；离线检查不能替代这些结果。

## 存储和限制

`StructuredDiagnosticLogger` 位于 `RizToolbox.Core.Diagnostics`，实现现有 SDK 的 `ILogger`。每次启动创建一个新会话，建议根目录为 `BepInEx/RizToolbox/diagnostics`：

```text
diagnostics/
  retention.lock
  <session-id>/
    session.lock
    session.json
    events-000001.jsonl
    snapshot.json
```

JSONL 每行具有独立 JSON 对象，包含 `schemaVersion`、`sessionId`、`sequence`、UTC `timestamp`、`level`、`eventCode`、`message`，以及可选 `context`、`exception`、字符串 `fields`。上下文可包含插件、能力、目标、RunId、场景代次和关联 ID。可据此过滤；当前没有诊断查看器。`ILogger.Log` 的事件代码固定为 `log`。

默认限制为：

| 项目 | 默认值 |
|---|---:|
| 最低输出等级 | Info |
| 单个 JSONL 文件 | 1 MiB |
| 每会话保留 JSONL 文件 | 4 |
| 保留自有会话 | 8 |
| 同等级、代码、消息、上下文的重复输出 | 每 5 秒最多 4 条 |
| 全部事件输出 | 每秒最多 200 条 |
| 重复判定键缓存 | 256 个，满后淘汰最久未访问项 |
| 消息 / 异常文本 | 2048 / 8192 字符 |
| 附加字段 | 最多 16 个，每值 512 字符 |
| 磁盘失败重试间隔 | 5 秒 |
| 日志待处理容量（含正在写入的事件） | 1024 条 / 4 MiB 计费载荷 |
| 显式刷新控制容量（含正在处理及停机请求） | 日志事件容量 + 2 |
| 显式 Flush / Dispose 默认等待期限 | 250 ms |

超过单文件大小的事件保留等级、代码和顺序号，以明确的超限提示替换大正文。抑制计数记录在内存快照中；每次被抑制都写一条摘要会重新制造日志洪峰，因此没有这样做。首次消息和前几条重复消息仍可诊断，之后的总量由计数反映。

轮转会删除最旧的自有文件。跨会话保留只处理具有匹配 `session.json` 的目录，逐文件检查白名单和链接，拒绝递归删除；遇到陌生文件时停止清理并记录存储失败。正在使用的会话持有锁，不会被其他实例清除。存储预算被活跃会话占满时，新实例保持可用但不写结构化文件。日志根目录的未知内容由用户管理，不计作本组件创建的文件。

生产端先过滤等级，再复制有界字符串和字段；一个后台消费者负责序列化、写盘、轮转、失败重试和存储句柄。队列满时丢弃最新事件并增加 `QueueDropped`，已接收事件保持顺序。`Enqueued` 表示入队，`Processed` 表示两个输出已经尝试处理，`Written` 与 `TextWritten` 各自只统计真实输出完成。快照中的 `PendingEvents`、`PendingBytes` 包含正在处理的事件。

`GetSnapshot` 只读内存计数，慢磁盘不会持有生产端的锁。构造返回不保证会话目录已完成初始化。日志 I/O 失败不会向游戏抛出异常，也不会递归记录自身错误；`LastError`、`DiskFailures`、`DiskSkipped` 可解释失效原因。选项或会话 ID 非法仍同步抛参数异常。异常文本的输出长度有上限，调用方自定义 `Exception.ToString()` 的执行时间无法被抢占；该限制不能当成实机成本保证。每条实际写入刷新操作系统流，不承诺突然断电后的持久性。

## 接入方式

```csharp
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

var diagnostics = new StructuredDiagnosticLogger(new DiagnosticOptions(diagnosticsRoot)
{
    TextLogPath = legacyToolboxLogPath // 可省略
});

ILogger pluginLog = diagnostics.ForContext(new DiagnosticContext(PluginId: pluginId));
pluginLog.Log(LogLevel.Warning, "Required capability is not ready.");

diagnostics.Write(LogLevel.Warning, "capability.waiting", "Run observer is waiting.",
    new DiagnosticContext(CapabilityId: "riz.runs", RunId: runId, SceneGeneration: sceneGeneration),
    fields: new Dictionary<string, string?> { ["reason"] = "controller-not-ready" });

var status = diagnostics.GetSnapshot(); // 仅内存快照，可放入宿主状态
diagnostics.RequestFlush();             // 游戏线程仅请求，不等待磁盘
// 离线工具或显式检查可有限等待：
var result = diagnostics.Flush(TimeSpan.FromSeconds(5));
// result.Completed 表示处理及刷新尝试完成；SnapshotWritten 表示原子快照写成。
```

`Flush` 等待调用时已接收的固定顺序号和一次刷新尝试，后来的事件或 `RequestFlush` 不会扩大该请求的等待范围。相同顺序号的并发调用共享一次尝试；`SnapshotWritten` 只表示该次尝试实际写成，后续成功不会改写此前的失败结果。`LastSuccessfulFlushSequence` 则记录真实成功覆盖的顺序号。显式控制请求使用事件容量加两项的固定队列，`PendingFlushAttempts` 和 `PeakPendingFlushAttempts` 显示实际占用；异步请求只保留一个合并目标。超时通过 `TimedOut` 报告，两种成功标志均不保证所有事件都写成。

`Dispose` 关闭准入并有限等待，超时后后台仍持有忙碌句柄，实际处理结束才释放；可观察 `Completion` 和 `ConsumerCompleted`。关闭后的请求共享末次尝试，不再增加待处理工作。进程意外退出可能留下尚未写出的事件或快照。`ForContext` 是借用视图，不能释放总日志器；SDK 插件仍只需引用 SDK。

宿主每次状态采集只生成独立的托管副本，一个消费者最多持有正在处理和最新待处理两份副本；旧待处理副本被合并时计入 `Coalesced`。owner thread 复制原始性能窗口，后台计算分位数、完整 JSON 和进程资源，再原子替换 `status.json`。采集和写盘错误分别记录在 `statusDiagnostics`，不阻断帧阶段；停机副本不重新读取已释放的游戏提供方。

在活动宿主的 `BepInEx/RizToolbox/status.json` 中，`sceneObservation` 为 `Observed` 时，`scene` 是最近一次有效观察的场景名，`sceneGeneration` 是同一会话内沿用的场景代次。观察器就绪但还没有有效场景名时为 `AwaitingSample`，观察器不可用时为 `Unavailable`，这两种状态的场景名和代次均为 `null`。场景随原有约两秒一次的状态采集从主线程缓存复制，停机状态省略这三个字段。

诊断选择每列表最多 256 行、总计最多 4096 行，单字符串最多 4096 字符、预算字符串总计最多 1 Mi 字符；截断通过 `statusCapture.TruncatedValues` 报告。插件资源计数使用最多 1024 个 scope 节点的共享选择预算。性能窗口另有 collector 自己的 series/sample 上限。这些是数据与遍历上限，无法证明任意同步调用已限时。实际控制分别记录 probe、capture、prepare、commit、confirm、restore、restore-readback 和 release；只记录真正到达的阶段，预算不打断原生调用。

可选文本兼容输出保留原有 `时间 [Info] 消息` 语法，独立轮转为 `toolbox.log.1` 等，使用与 JSONL 相同的单文件和文件数量限制。两种输出共享限流决定，存储失败互相独立。文本文件跨会话混合，因此证据导出不会包含它；原本已超限的文本文件会在首次写入时被删除以恢复大小上限。使用旧日志偏移量的验证脚本需要考虑轮转。

人工读取活动 JSONL 文件时，需要 `FileShare.ReadWrite | FileShare.Delete`；普通 `File.ReadAllText` 不允许已有写句柄，可能返回共享冲突。下面的导出工具已经按活动文件的读取方式实现。

## 导出一个会话

先从宿主 `status.json` 的诊断快照取得 `sessionId`；也可查看 `diagnostics` 下的目录和 `session.json`。导出必须显式指定一个会话，不自动把所有历史记录混在一起：

```powershell
pwsh -NoProfile -File scripts/Export-Diagnostics.ps1 `
  -DiagnosticsRoot 'D:/SteamLibrary/steamapps/common/Rizline/BepInEx/RizToolbox/diagnostics' `
  -SessionId '<选定会话 ID>' `
  -OutputDirectory 'C:/Users/71957/Desktop/riz-toolbox-evidence'
```

目标目录必须是新目录且位于日志根目录之外。默认输入与输出预算均为 16 MiB，最多读取 16 个事件文件；可显式设置 `-MaxTotalBytes`（最高 64 MiB）和 `-MaxEventFiles`（最高 64）。源文件会先经过有界读取、会话身份检查和 JSON 解析，验证成功后才创建目标目录。输出的 `export-manifest.json` 包含每个文件的 SHA-256、字节数、事件数量与导出时间。

只输出选定会话的 `session.json`、事件 JSONL、可选快照及导出清单。元数据和快照采用字段白名单，快照中的本机目录不导出；原始存档、账号文件、设置、成绩导出、全局状态和其他会话不会读取或复制。插件自行写进日志正文或附加字段的信息仍属于日志内容，分享前应检查。

活动会话允许导出，但快照与事件不保证来自同一个原子时刻；轮转或删除造成的读取失败会明确报错，重新导出即可。最后一条尚未写完的 JSONL 记录会丢弃，并在清单中计数；其他格式错误或混入其他会话会拒绝整个导出。

## 验证

```powershell
dotnet run --project tests/RizToolbox.Diagnostics.Tests/RizToolbox.Diagnostics.Tests.csproj -c Release
pwsh -NoProfile -File tests/RizToolbox.Diagnostics.Tests/Test-Export.ps1
```

测试覆盖结构字段、关联上下文、并发顺序、单会话与跨会话轮转、活跃会话保护、未知文件保留、重复与唯一消息洪峰、大小上限、磁盘及路径错误、等级过滤、文本兼容输出、导出会话隔离和预算拒绝。该测试集不启动游戏、不安装文件，也不构成性能基准。
