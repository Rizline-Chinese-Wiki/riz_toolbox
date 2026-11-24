# LOCAL 初始化引用、早期 Update 与 Steam 尾调

分析日期：2026-10-23（UTC+8）；flavor=null；范围见 [scope.md](scope.md)。

固定镜像中，`LevelControl.Start` 在方法前段经 LevelStart delegate 调用 getter，在方法末段才设置 loadOver=true。早期 `Progress.Update` 并不等待 controller.Info；它直接读自身序列化 info，并在实际 loadOver=false 时返回。Level 场景磁盘对象已包含 info/progress 的相互引用，但剥离的自定义 type tree 使字段标签仍须结合固定 metadata 的声明顺序解释，不能等同于运行时对象已绑定或调度顺序。新增的关键遗漏是 Start 本身在 loadOver=true 后调用两项 Steam Timeline API，因此 LOCAL 初始化需要独立的精确 Start 上下文。此次仅做有限静态检查，没有运行游戏或证明 LOCAL 隔离成立。

## Evidence

| ID | observed_at | source_type / source_ref / artifact_path | content_hash | raw_excerpt | linked_workitem / supersedes |
|---|---|---|---|---|---|
| E-imports | 2026-10-23 | file / triage.json / triage.json | 见 evidence-manifest.json | AMD64 native PE；导入/导出/动态加载与文件、线程等分类 | LOCAL 初始化静态补证 / none |
| E-native | 2026-10-23 | file / 固定 GameAssembly / boundaries.json、disassembly.txt | boundaries `528842771271119b1d2e8386bbe8ce47810b054f1a747da96a86aa7e14e871d3` | Start: 3EA935 getter；3ECDFE loadOver=1；3ECE0E/3ECE30 Timeline | LOCAL 初始化静态补证 / none |
| E-metadata | 2026-10-23 | file / 固定 dump.cs、script.json / metadata-excerpts.txt | 见 evidence-manifest.json；原件 SHA 见 README | LevelControl.info+20；info.progress+28；progress.info+20；Tooltip(string,float) | LOCAL 初始化静态补证 / none |
| E-scene | 2026-10-23 | file / level6、globalgamemanagers.assets / scene-references.json | `28d0fb4d33a4992490903a230f8412299cb3fcbe3e838d87579b17138e6227d7` | GO58 上 info219、progress189、LevelControl188；原始 PPtr 相互一致 | LOCAL 初始化静态补证 / none |
| E-check | 2026-10-23 | command / verify-evidence.py / evidence-check.json | 见 evidence-manifest.json | 9 ranges / 27 anchors / 3 serialized objects；普通 CFG getter 支配 loadOver/初始化 Timeline | LOCAL 初始化静态补证 / none |

所有 Evidence 的复现命令为 README 中的验证命令。E-native 原件哈希为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，具体范围逐条固定。原始提取器身份固定于 extractor-provenance.json；本次并未创建新反编译器或进行映像全局函数扫描。

## Findings

### F-001：入口已具备可核对的 scene 引用

- severity: n/a_re；category: reverse_algo；status: validated。
- evidence_ids: [E-native, E-metadata, E-scene]。
- location: Level scene GO58；LevelControl.Start@3EA92C；NormalMode.GetInsideLevelParams@3BEF51/3BEF81。
- impact: Getter 保存实参到 controller.Info 后立即读 `info.progress` 并写 `playOffset`。因此宿主在实际精确 getter 的原调用前核对同一 info 与 reciprocal progress identity 有明确锚点；无需先调用 getter 来“创建”这两条引用。
- confidence: high（原始字节、callsite 和同场景引用）；medium（剥离自定义 type tree 下的字段顺序标签解释）。
- repro_steps: 运行 verify-evidence.py；查看 scene-references 的 rawHex、scriptPathId 和 metadata-excerpts 对应字段。
- remediation: 独立 root 实际实参，验证 Type/字段 offsets/Unity 活性及反向相等；不把磁盘 pathID 当运行时 pointer。
- residual: 不能由磁盘序列化引用证明某一 runtime 场景/Prefab 当前仍是该对象，亦不能证明 Unity 回调顺序。

### F-002：更早的 first Update 不构成已证明的终止/Timeline 路径

- severity: n/a_re；category: reverse_algo；status: validated。
- evidence_ids: [E-native, E-metadata, E-check]。
- location: Progress.Update@40108D/4010AE/4010B5/401617..401630。
- impact: 当实际 info 非空且 loadOver=false，`je 401617` 后仅恢复 XMM7/XMM6/RDI/RSI/栈/RBX 并 ret，不调用 LevelOver、Timeline 或 AudioSource.set_volume。入口 lazy metadata 初始化在此条件判断之前，故不能泛称整次调用“绝对无副作用”。first Update 是否会早于 getter，静态证据没有排除。
- confidence: high（条件内的直接机器路径）；对 Unity 实际首次回调排序为 unknown。
- repro_steps: 核对 verifier 中 epilogue mnemonic 序列，以及 instructions 4010AE、4010B5。
- remediation: 宿主必须实际读取并约束当前 loadOver；不能从 C# 默认值、同 GO 或通常的 Start 顺序推出安全性。
- residual: 未搜索其它方法写 loadOver 的完整集合；不推广到其它 Progress 方法或间接被调用者。

### F-003：初始化 Start 存在两项不受 Progress around 覆盖的 Steam 调用

- severity: n/a_re；category: design；status: validated。
- evidence_ids: [E-native, E-metadata, E-check]。
- location: LevelControl.Start@3ECDFE、3ECE0E、3ECE30；SteamTimeline.SetTimelineTooltip@48A980。
- impact: 普通函数内 CFG 中，getter 调用支配 `loadOver=1` 及两项直接 Timeline 调用。Start getter 前的已解析直接调用中未见 SteamTimeline；未递归间接或引擎调用。初始化尾部直接调用 GameMode(1) 与 Tooltip；Tooltip 文本另一分支也合流同一调用点。保护仅围绕 Progress.Update 时，这两个调用不会在该上下文内。
- confidence: high（固定机器字节及普通 CFG）。
- repro_steps: 运行 verifier 的 cut-getter reachability 检查；查看 Tooltip wrapper 对 RCX/XMM1 的保存和尾调。
- remediation: 新增精确 LevelControl.Start 上下文，并在实际精确 LOCAL getter 原调用前将父帧归属。保护持续到该 Start original 完整返回；同时覆盖 GameMode 和 Tooltip 的明确 ABI。
- residual: CFG 不模拟异常边、重入及间接被调用者；未实现绑定、未运行 LOCAL 激活、未证明 Steam 零调用。

## Path

### P-001：可供实现的 Start 父帧归属路线

- path_type: callflow。
- start: LevelControl.Start 实际入口。
- goal: 在 LOCAL getter 的原调用前取得确切 info/progress 归属，使初始化尾调保留在精确 LOCAL 上下文内。
- steps:
  1. 为当前 LevelControl 建线程/嵌套归属明确的未定父帧，原方法执行一次，finally 退出。此时不凭任何“有 LOCAL”状态抑制调用。— evidence: E-native、E-metadata — finding: F-001。
  2. 实际 LOCAL controller 的 GetInsideLevelParams 接到 info 时，验证 info 与父帧 `[LevelControl+20]` 精确相同；核对 info.progress 及 progress.info 反向一致，独立 root 并发布纯身份映射。— evidence: E-native、E-scene — finding: F-001。
  3. 完成归属后再放行 getter original；其返回后 Start 继续构建、设置 loadOver=true、调用 GameMode/Tooltip。为这两个调用提供当前精确 LOCAL 初始化帧权限，不能仅依赖“已发生终止”。— evidence: E-native、E-check — finding: F-003。
  4. 较早 Update 若实际 loadOver=false，只对本函数有限早退路径作判断；frame/映射未发布、未知/嵌套/退出异常均按独立未知责任处理，不能据本报告猜测调度。— evidence: E-check — finding: F-002。
- residual_risks: Start 入参快照何时 root、父帧归属冲突、getter original 失败后的对象责任、静态 API ABI 的宿主实现、Start 间接 Steam 调用、异常边和全局清理均未在本任务验收。

## 限制与后续最小证据

本轮已补取必要的 10,656 字节单一 LevelControl.Start、464 字节 Progress.Start.MoveNext、128 字节等待谓词及 64 字节 Tooltip wrapper，未将“大致场景顺序”充作证据。若实现要求证明 first Update 的实际先后，下一步应在授权恢复实测后只记录 exact LevelControl.Start/getter/Progress.Update 的入口/退出、Unity frame 与线程，不读取整段运行内存。若要求初始化 Steam 全覆盖，需要继续对 Start 已见的明确间接调用逐项收敛，当前无直接调用不等于间接链不存在。

Timeline：定位已有八范围与三个 scene 对象 → 加入 Tooltip wrapper 九范围 → 新增 27 锚点和普通 CFG 检查 → 验证通过；无构建、安装、进程或 GUI 操作。
