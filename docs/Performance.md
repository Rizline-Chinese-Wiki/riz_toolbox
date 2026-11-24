# 性能观察、基线与比较

`Measure-Foundation.ps1` 只读取已经运行的进程及宿主 `status.json`，写入单独的新证据文件。它不启动游戏、安装框架、切换插件、发命令或修改游戏设置。`Compare-FoundationPerformance.ps1` 默认只输出观察报告；必须显式传入 `-ReleaseGate` 才请求发布门槛判定。

本轮没有采集真实游戏的四种基线，没有校准毫秒预算，也没有完成 20 轮真实启停/场景切换。附带阈值示例明确为 `uncalibrated`，不能得出发布通过。B18 仍待这些真实证据。

## 四种条件保持相同游戏工作负载

| Condition | 操作者准备的真实条件 | 采集来源 |
|---|---|---|
| `GameOnly` | 原游戏，不装入被测框架或探针 | 系统 Process；不需要也不读取 host status |
| `EmptyFramework` | 空框架，零个已启用普通插件 | Process + 同一宿主会话的新 status revision |
| `TwoProbes` | 两个实际探针，传入它们的真实 manifest ID | 同上；每份 status 的 Enabled 集合须正好是这两个 ID |
| `StressPlugin` | 实际压力插件及其所需探针，列出所有启用 ID | 同上；每份 status 的 Enabled 集合须与声明相同 |

GameOnly 的框架缺席需要操作者确认，Process 采样本身无法证明它。其余条件拒绝正在 Enabling/Disabling、Faulted、Waiting 的插件状态。Disabled 插件可以仍被登记，资源计数会保留它们的实际条目。

在同一台机器、相同游戏构建、分辨率、帧率限制、VSync、场景和操作流程下分别采集。先暖机，再进行同样时长的工作负载。每个条件保存独立运行；改变分辨率、显示设置、机器或采样日程后重新建立基线。机器记录包括电脑名、OS、架构、逻辑处理器、CPU、物理内存与 GPU 名称；硬件查询不可用会明确记录。显示/FPS 参数是操作者实际设置的声明，工具不会推断它们已被游戏采用。

下面命令在项目根目录运行，要求 PowerShell 7.2+。输入现有游戏的 PID 和实际设置；采样期间由操作者维持所声明的工作负载。

```powershell
New-Item -ItemType Directory -Path ./performance-evidence -Force | Out-Null
$capture = @{
    GameProcessId = [int](Read-Host '现有游戏 PID')
    Scenario = Read-Host '固定场景/操作流程的版本标识'
    Workload = Read-Host '本次实际操作流程，四种条件须保持相同'
    ResolutionWidth = [int](Read-Host '实际分辨率宽度')
    ResolutionHeight = [int](Read-Host '实际分辨率高度')
    FrameRateLimit = [int](Read-Host '实际帧率上限，0 表示无限制')
    VSyncCount = [int](Read-Host '实际 VSync count，关闭填 0')
    DurationSeconds = 120
    WarmupSeconds = 30
    IntervalMilliseconds = 500
}
& ./scripts/Measure-Foundation.ps1 @capture -Condition GameOnly `
    -OutputPath ./performance-evidence/game-only.json
```

上述 120 秒、30 秒暖机、500 ms 轮询是采样日程示例，不是已校准预算。其余三个条件在各自真实环境准备好后运行；若重启游戏，先更新 `$capture.GameProcessId`。不要复用旧 PID。

```powershell
$status = Read-Host '当前被测宿主 status.json 完整路径'
$capture.GameProcessId = [int](Read-Host '当前游戏 PID')
& ./scripts/Measure-Foundation.ps1 @capture -Condition EmptyFramework -StatusPath $status `
    -OutputPath ./performance-evidence/empty-framework.json

$twoProbeIds = @((Read-Host '当前两个实际探针的 manifest ID，以逗号分隔').Split(',') | ForEach-Object { $_.Trim() })
& ./scripts/Measure-Foundation.ps1 @capture -Condition TwoProbes -StatusPath $status `
    -ExpectedPluginIds $twoProbeIds -OutputPath ./performance-evidence/two-probes.json

$stressIds = @((Read-Host '压力条件下所有已启用 manifest ID，以逗号分隔').Split(',') | ForEach-Object { $_.Trim() })
& ./scripts/Measure-Foundation.ps1 @capture -Condition StressPlugin -StatusPath $status `
    -ExpectedPluginIds $stressIds -OutputPath ./performance-evidence/stress-plugin.json
```

每条命令都单独采样；先实际改变条件，再执行对应命令。输入相同的 ID 字符串不能替代真实探针或压力插件。输出文件必须尚不存在，父目录须已存在。

采集器固定实际 PID 和进程启动时间，拒绝进程退出/身份改变。host 样本固定 `performance.SessionId` 与 `diagnostics.SessionId` 两个独立会话 ID，拒绝旧进程数据、超过默认 10 秒的 status、未来时间、会话变化、revision 回退和时间倒退。相同 revision 不重复计样；只有递增 revision 进入 `hostSamples`。宿主通常两秒写一次 status，因此 500 ms Process 轮询不会产生四倍宿主样本。

必须至少有两个 Process 样本；宿主条件还必须至少有两个新 revision。零/一样本、JSON 损坏、资源字段不可用和不完整数据会导致失败，不产出成功证据。默认最长可请求一天，另有 Process 200,000 点、host 20,000 点、JSON 64 MiB 上限；长时间采样应增加轮询间隔。

## 指标如何解释

证据使用 `schemaVersion: 1`、`metricDefinitionVersion: foundation-observation/1`。保存原始 `processSamples`、精简的 `hostSamples`、实际会话/进程身份、日程、机器和设置；不会复制 status 的日志、存档、账号、任意插件数据或其他诊断会话。比较器从原始曲线重新计算指标，不相信输入文件已有的 metrics 字段。

| 指标路径 | 定义及单位 |
|---|---|
| `process/workingSetBytes/*`、`process/privateBytes/*` | 系统 Process 工作集、私有字节的采样统计 |
| `process/handleCount/*`、`process/threadCount/*` | OS 句柄及线程数的采样统计 |
| `process/cpuMilliseconds/delta` | 最后与最初 Process.TotalProcessorTime 差，所有线程 CPU ms；不是帧耗时或 CPU 百分比 |
| `host/managedBytes/*` 等 | status 的托管内存、工作集、私有字节、OS 句柄数的采样统计 |
| `host/totalAllocatedBytes/delta` | GC 全进程累计分配计数的观察区间差；不等于仍存活字节 |
| `host/gen0Collections/delta` 等 | GC 各代累计计数的观察区间差 |
| `plugins/retainedOwnedResources/*` 等 | 各插件计数合计；同时保留 scopes、cachedServiceViews |
| `plugin/<转义 ID>/retainedOwnedResources/*` 等 | 全采样期间均存在的单插件序列；原始逐插件条目也保留 |
| `callback/<转义 owner>/<转义 operation>/maxWindowP50Milliseconds`、P95、P99 | 所见宿主窗口对应分位数的最大值，即最差已观察窗口 |
| 相同 callback 路径的 `TotalSamplesDelta`、`TotalAllocatedBytesDelta`、`OverBudgetDelta` | 该 series 首末所见生命周期计数差；嵌套回调会重复计入其上层 |
| `host/droppedSeriesSamples/maximum` | 观察到的丢弃 series 样本累计计数最大值，可能包含采集前的丢弃 |

上表通用的 `*` 包括 `p50`、`p95`、`p99`、`maximum`、`delta`、`slopePerSecond`、`monotonicGrowth`。p50/p95/p99 用原始采样点排序后的 nearest rank，rank = ceil(n×p)，从 1 开始；它们描述采样的字节数/计数分布。delta 是末值减首值；slopePerSecond 是以实际秒数作自变量的最小二乘直线斜率；monotonicGrowth 为 1 表示没有下降且至少一次上升。短序列的这个标记不构成泄漏证明。

回调的 p50/p95/p99 来自 Core 每条 series 的有界近期窗口，默认每条最多 2048 点。窗口可能重叠，不能把各窗口 p99 求平均，不能从这些摘要还原全程 p99。本工具只保留窗口摘要并取最大值，命名明确为 `maxWindowP99Milliseconds`。status 中 MaximumMilliseconds 是生命周期最大值，也不等同于单个窗口最大值。窗口大小和 callback budget 配置变化会使比较被拒绝。

这套采样没有全帧时间、GPU 时间、present 时间或每帧原始分配。OS 句柄数也不是 IL2CPP GC root 数；RetainedOwnedResources 是 scope 所保留的托管所有权条目数，并非精确的活跃 Unity 资源数。若需要帧/GPU 分位数或原生 root 泄漏证据，应另用适合该目标的逐帧测量与 root 统计，不能用这里的 p99 代替。

## 查看曲线与观察比较

```powershell
$sample = Get-Content ./performance-evidence/stress-plugin.json -Raw | ConvertFrom-Json
$sample.processSamples | Select-Object elapsedSeconds,workingSetBytes,privateBytes,handleCount,threadCount |
    Export-Csv ./performance-evidence/stress-process-curve.csv -NoTypeInformation
$sample.hostSamples | ForEach-Object {
    [pscustomobject]@{ capturedAtUtc=$_.capturedAtUtc; revision=$_.revision;
        managedBytes=$_.processResources.managedBytes;
        allocatedBytes=$_.processResources.totalAllocatedBytes;
        ownedResources=($_.pluginResources | Measure-Object retainedOwnedResources -Sum).Sum }
} | Export-Csv ./performance-evidence/stress-host-curve.csv -NoTypeInformation

& ./scripts/Compare-FoundationPerformance.ps1 `
    -BaselinePath ./performance-evidence/game-only.json `
    -CandidatePath ./performance-evidence/empty-framework.json `
    -OutputPath ./performance-evidence/overhead-observation.json
```

无阈值时结果为 `Observed`、`releaseEligible:false`。跨条件只比较双方都有的指标，GameOnly 与空框架的共通项是系统 Process 指标。相同条件比较还要求插件 ID 集合一致；所有比较要求机器指纹、metric 版本、场景、工作负载、分辨率/FPS/VSync、暖机、请求时长和轮询间隔相同。

## 校准阈值与显式发布门槛

阈值格式见 [schema](performance-thresholds.schema.json) 与 [未校准示例](performance-thresholds.example.json)。示例没有编造任何毫秒或资源预算，直接使用它只能观察，ReleaseGate 会失败。

真实采样完成后，先检查曲线、异常、每种条件的重复运行波动和外部干扰，再决定具体指标的允许值。在校准说明中记录选择依据、机器和游戏构建、操作流程、采样日程，以及为什么这些上限覆盖正常波动。每次改变预算必须更改 thresholdVersion，不能把本机预算直接用于不同机器。

把真实四条件证据登记为 `calibration.sources`，每项包含源文件路径（相对阈值文件或绝对路径）及其实际 SHA-256，可用 `Get-FileHash -Algorithm SHA256` 计算。校准时必须满足：

- `state` 为 `calibrated`，有实际 `calibratedAtUtc`、说明和不小于 10 秒的 `minimumDurationSeconds`；10 秒只是格式下界，不是性能验收日程。
- 源文件覆盖四个条件，均标记为真实只读观察，哈希正确，并与比较目标的机器、工作负载和日程一致。fixture 不能校准。
- `rules` 对具体 baselineCondition/candidateCondition 和实际 metricId 定义上限，不能对缺失指标默认为 0。

规则可指定 `maximumAbsoluteValue`（candidate ≤ 上限）、`maximumIncrease`（candidate−baseline ≤ 上限）、`maximumIncreasePercent`（增量÷baseline×100 ≤ 上限）。一个规则有多个上限时全部须满足；百分比要求 baseline > 0，零/负基线应选绝对值或增量。metricId 来自观察报告的实际 differences 列表，单位沿用上表。

```powershell
$thresholdFile = Read-Host '已经由真实四条件证据校准的阈值 JSON 路径'
& ./scripts/Compare-FoundationPerformance.ps1 `
    -BaselinePath ./performance-evidence/game-only.json `
    -CandidatePath ./performance-evidence/empty-framework.json `
    -ThresholdPath $thresholdFile -ReleaseGate `
    -OutputPath ./performance-evidence/overhead-release-gate.json
```

只有校准源通过校验、规则覆盖此条件对、比较文件为实际观察、采样时长足够且所有规则通过时，显式门槛才输出 `Pass`。不符合资格输出 `NotEligible`，超阈值输出 `Fail`，两者均令 PowerShell 命令失败；提供 OutputPath 时仍先保存可审阅 JSON。默认不启用 ReleaseGate，既不设置 host budget，也不影响插件运行。校准引用证明文件来源和一致性，实际操作真实性仍需要操作者证据。

## 20 轮真实启停与场景切换

为此单独定义一个完整且有版本的操作流程，例如每轮实际启用同一组探针、进入指定真实场景、返回、停用，然后等待到同一恢复检查点。持续运行采集器覆盖 20 轮及最后的稳定阶段；操作者逐轮记录真实动作、开始/结束时间、场景与异常。不要用一个 PowerShell 模拟循环生成“20 轮”证据。

把逐轮时间记录与导出的曲线对齐，在每轮相同恢复检查点读取 scopes、RetainedOwnedResources、服务缓存、OS 句柄和内存；再检查端点差、趋势及最终恢复。活动中的锯齿增长、GC 波动和资源缓存要结合真实动作解释，不能只看线性斜率。另核对原生 root/Unity 对象的专门统计。本工具不控制场景，也不从时间曲线推断实际发生过 20 轮；发布 Pass 仅表示已列指标通过阈值，不自动完成 B18 的场景压力验收。

## 工具自检与证据边界

```powershell
& ./scripts/Test-PerformanceScripts.ps1
```

2026-10-20：10/10 检查通过。覆盖 raw nearest-rank/OLS、fresh revision 去重、旧 status/错 PID/混合会话/插件集合拒绝、零 host 样本、最差窗口 p99、不相信序列化摘要、未校准门槛拒绝、fixture 校准拒绝、不同场景/缺失指标/旧导入证据拒绝、schema，以及零回调/零资源的空框架。唯一实时采样是当前 PowerShell 自检进程 0.35 秒，没有启动被测游戏。

| 证据 | 结论 | 路径 |
|---|---|---|
| `PerformanceContracts.cs`、`CallbackPerformanceMonitor.cs` | window nearest-rank、生命周期分配计数及观测边界 | Core monitor → `status.performance` |
| `Test-PerformanceScripts.ps1` 的 fixture 与自进程检查 | 脚本采集、拒绝及比较分支可执行 | Measure → 原始曲线 → Compare → 版本阈值 |
| 当前没有真实四条件/20轮数据 | 不能宣称已达性能预算或完成 B18 | 由上面的实际采样与操作记录补齐 |
