# 晚装普通 Hook 与播放镜像组合

## 当前候选与结论

当前为 [receiver-read 运行包](../artifacts/delivery-20261116/RizToolbox-0.3.0-receiver-read-preview-20261116.zip)，37,311,070B，SHA256 `bfaa4effc399c07114a44ebb7524ee97e241a939a43da0141444a0f23519bbe8`。35个成员、25个运行文件已逐字节读回，四类新proof、执行DLL与源hash匹配；[receipt](../artifacts/delivery-20261116/receiver-read-preview-receipt.json) 仍为 `realGameVerified=false`。1243项产品及既有runner输入冻结，新增receiver runner另固5源。noeffect及更早运行均为历史，不能验收本轮新字节。

本轮四个产品文件为Runtime的 `ExactHookBroker.Playback.Group.cs`、`NativePlaybackSingleReturnBridge.FreshSource.cs`，Adapter的 `NativePlaybackStockRootedSource.cs`、`NativePlaybackStockClockRouter.Idle.cs`。`HasFirstPausedReadbackCandidate` 只筛原基线允许的不可变Role Pause/ProgressPlay，保留fault/drain及fresh；同一次nativeRead的status/wire诊断已实装，保留全部原拒绝，没有新增native调用。新11步全0及四类owned专项均已完成，但不代表正式stock fresh-source或实际Adapter正向。

安装自然退出0（原回执 `42412/854f26`），23:24:25.6404963本地时间25/25匹配，官方Steam于23:24:25.6497434请求启动。初始14272于23:24:27.7371666启动，只到Runtime bridge awaiting Unity；随后Steam显示正常游戏/Editor启动选项，CUA选择默认“玩Rizline”并开始，14272消失，新实际2696于23:26:31.7456895启动。最新CLR于15:26:59.1468375 UTC写出诊断session `20261116T152658724Z-47d0eeca893948f2b8dd9006aed085d2`；CUA已观察到Rizline.exe白色初始化画面。这里只确认安装与新启动，未验菜单、source组完成、实际Pause、Ready或Speed/Seek；早期start-a原件保留，不将14272视为完整游戏。

下一路径是用本轮新包独立记录source接管、Host group验证和fresh receipt/lifetime诊断，再验证Pause→Ready→Speed/Seek。B01–B19、全SDK及production尚未整体完成，配套controls SDK公开合同未改；续接点见 [CURRENT-CHECKPOINT](../.docs/CURRENT-CHECKPOINT.md)。本次文档更新仅读已有原件，未构建、执行检查或操作游戏/账号。

## 证据 → 发现 → 下一路径

以下为receiver-read本轮文件原件；时间及复现参数以各result/日志为准，旧原件不改。

| E-id / source_ref | content_hash（SHA256） | 观察与边界 |
| --- | --- | --- |
| E-011 [产品/既有runner冻结](../work/game-seek-20261115/receiver-read-r1-source-freeze.json)、[11步结果](../work/game-seek-20261115/receiver-read-r1-result.json) | freeze `d0f9f03d8f4716db0706e3c2b1a1441dfcab5b24654846ddd17ad47196dc339d`；result `c630b2a55bf55ceb8de641dba31514368f8fbbb0e19f794a5bccf9f34ee6f83d` | 1243项冻结；原 `90578/145e55`，11步全0；Hooks210、Gameplay201、clock14、host3及旧fresh机制31，31项未执行正式Adoption/stockRuntime |
| E-012 [receiver runner 5源冻结](../work/game-seek-20261115/receiver-group-checks/runner-source-freeze.json)、[receiver-group-checks/checks-r1/result.json](../work/game-seek-20261115/receiver-group-checks/checks-r1/result.json) | freeze `2aed970ce87374b67b25b5b8775759fa6c76502647036e9193d066f1500286a3`；result `d686f9792cd197451c9a0330076954f4fc9660c4b9f4665d768f6d18677c391d` | 原 `41188/1dfb2c`，两mode50断言，各真实23/23+3成员pair；wrong-method/错player class/null player保原异常、同实际组active且三次恢复成功；native getter byte tamper拒绝并sticky，恢复不复活 |
| E-013 [occurrence checks-receiver-r1](../work/game-seek-20261115/new-occurrence-diagnostic-checks/checks-receiver-r1/result.json) | `8c896a8bb7e56696e2accf522fb4af93799e76991301a8e3ae3d9931ea0446a8` | 原 `99491/2b403c`，两case66断言，各真实23/23；no-op无票，同producer后续实际PauseLeaf可issue；copied/已释放journal拒绝，各4根最终0 |
| E-014 [focused checks-receiver-r1](../work/game-seek-20261115/focused-owned-supporting/checks-receiver-r1/result.json) | `e6365e309218effc6df44b8e91b3935d1ea15f66591a7e8ac4edfde37c5c36cf` | 原 `74158/18a305`，五mode138断言，各真实23/23+3成员pair；四类篡改恢复仍sticky |
| E-015 [receiver-read-traversal-r1](../work/game-seek-20261115/receiver-read-traversal-r1/result.json)及raw/seh/interference日志 | `99b04fe069dbd3f2f580f5f7fa0c02396c210351939183b205a4df7f596bad4c` | 原 `85880/61bda0`，四步全0；raw99+SEH44+interference61=204断言，各child真实12/12 Prepare/Commit |
| E-016 [receiver-read-preview-receipt.json](../artifacts/delivery-20261116/receiver-read-preview-receipt.json) | `f1ca5eef10bcc3f811dbc37cbfc2d287665fe345898e9094d7eedc7a458601f4` | 原 `05de64` 退出0；ZIP37,311,070B、35/25逐字节读回，四类proof与当前执行DLL/源hash匹配，realGameVerified=false |
| E-017 [安装观察](../work/game-seek-20261115/install-receiver-read-observation.json)、[安装回执](../work/game-seek-20261115/install-receiver-read-r1-command.json)、[官方启动](../work/game-seek-20261115/launch-receiver-read-request.json)、[新启动观察](../work/game-seek-20261115/receiver-read-r1-start-a-observation.json) | 安装观察 `2b8561752c9b20041a4339efb64ee5fdbd4e8ec3c62af00aff024854721b0528`；启动观察 `354ce05de5160f6e35b89e30b32bb23343209c311783995b1f111f0e937fbba4` | 原安装自然0，25/25；14272新CLR已启动，source组尚未完成；仅安装/启动事实，GameVerified=false |
| E-018 本轮CUA/进程启动观察（root记录，后续文件另存） | n/a；早期start-a不覆盖 | 默认“玩Rizline”开始后旧14272消失，新2696及新CLR/session出现，CUA白色初始化画面；只证明后续启动，不证明菜单或游戏验收 |

E-012至E-015均为本轮新DLL新运行，无stock foundation、Unity、正式Adoption、Clock读取、game或production；50项只证明owned组receiver输入及篡改处理，不是正式stockfresh正向。66项的Before清marker仍仅静审覆盖，不作动态单独通过声明。

| F-id / evidence_ids | 发现、location与status | 下一路径 |
| --- | --- | --- |
| F-006 / E-011、E-012 | Runtime Group receiver输入保原拒绝及恢复、tamper sticky已通过；`severity=n/a_re`、`confidence=high`、`status=validated`，限owned机制 | 新Adapter正式正向仍待实机，不能把50项提升为正式stockfresh正向 |
| F-007 / E-011、E-013、E-014、E-015、E-016 | 本轮构建、四类owned机制与交付输入已关联；`severity=n/a_re`、`confidence=high`、`status=validated`，限原件范围 | 用新包单独验证Pause/Ready/SpeedSeek，保留无foundation等边界 |
| F-008 / E-016、E-017、E-018 | 新包安装、初始14272及后续2696启动已区分；`severity=n/a_re`、`confidence=high`、`status=validated`，限安装/启动 | 等2696完成source组初始化，实际游戏验收仍未完成 |
| F-005 / 下文E-009、E-010 | 历史noeffect接管返回后仍阻塞，最终fresh receipt/lifetime失效而Withdrawn；`severity=n/a_re`、`confidence=high`、`status=validated`，限旧日志/栈 | 本轮用同一次nativeRead的status/wire诊断复核，不据旧栈猜native role或验新源码 |

P-002（`path_type=callflow`）：本轮receiver输入在实际23组上保原异常/恢复，tamper仍sticky（E-012，F-006）；11步及四类owned proof与新包绑定（E-011至E-016，F-007）；新包25/25安装、初始14272退出后实际2696启动（E-017/E-018，F-008），接下来独立记录source接管→同次nativeRead诊断→真实leaf/execution票/readback→Clock/Ready。source阻塞、receipt/lifetime失效、私有no-effect分支直接正向、Pause/Ready/Speed/Seek和整体SDK/production仍未验收。

离线复核仅读原件，不执行runner；从项目根运行，下例对应E-012，其余替换为表中明确路径：

```powershell
Get-Content -LiteralPath 'work/game-seek-20261115/receiver-group-checks/checks-r1/result.json' -Raw
Get-FileHash -Algorithm SHA256 -LiteralPath 'work/game-seek-20261115/receiver-group-checks/checks-r1/result.json'
```
## 历史证据与时间线

以下均只证明对应旧候选；noeffect、旧idle日志、旧66/138/204及旧游戏不能验收receiver-read。

| 历史候选与原件 | 已观察事实 | 保留限制 |
| --- | --- | --- |
| E-009 [noeffect-read-r1-game-result.json](../work/game-seek-20261115/noeffect-read-r1-game-result.json)及[after-exit观察](../work/game-seek-20261115/noeffect-read-r1-after-exit-observation.json) | 旧50960/session `7ad338808cd34d5996c77f89b11ad8b7`/run `caa0a5ebdcf44e5bbfac75f791a0a321`，Greatness Earth/Axya/EASY4/FREE；首Clock19.1339981秒，14:52:58.1334712→14:56:27.3200752 UTC接管209.1866040秒；14:57:39 paused仍Waiting，未请求SDK Pause；Resume仅一次14:58:25.2204929→14:59:24.2821517 Applied，59.0616588秒 | 随后仍阻塞，15:02:08 Draining、15:02:13 Withdrawn，fresh receipt/retained lifetime changed；role未知，private decline未直接动态验收，Ready/SpeedSeek未过。正常Alt+F4后23:05:59.2300845进程不存在，无Stop/RunEnded，OS退出码未知。结果SHA `50ccced71634cb5525a08939c51126c8aa9ea6abec857edf6650c33ab4174445` |
| E-010 [noeffect after-Resume栈](../work/game-seek-20261115/noeffect-read-r1-after-resume-a-stack.log)；[历史noeffect receipt](../artifacts/delivery-20261116/noeffect-read-preview-receipt.json)与[11步结果](../work/game-seek-20261115/noeffect-read-r1-result.json) | 旧栈位于Host clockProfile→group，未直接观察native/candidate role；旧1243输入、11步全0及当轮66/138/204均保留在各原件/receipt | 栈SHA `574564dccca0848a4ec0991cfb859b287ff44dca8fa03167231cca30d4a51b34`；旧包37,309,425B，SHA `0b37e7ef5c7960a663ba96cb6a34858b9d794e090109d344170dfabc8ccfb12b`。noeffect原件不变，不代替本轮新源码/新DLL运行 |
| [idle-read-r1-game-result.json](../work/game-seek-20261115/idle-read-r1-game-result.json)、[after-exit观察](../work/game-seek-20261115/idle-read-r1-after-exit-observation.json)及原log | 旧49744、session `1ba1da0a328a4dc090ff9ee918ac303e`、run `67708c2becd54eca87499f52ba52bb85`；首次Clock19.3672430秒，接管211.6562298秒后WaitingForPause；一次SDK Pause点击timeout，未重复；随后exact leaf mismatch→Withdrawn，实际paused=true | 无有效SDK票，Clock未armed，ReadyExperimental/SpeedSeek未过；rootRole未知，歌曲结束只属推断。正常Alt+F4后22:26:42.9148055进程不存在，无Stop、无OS退出码，也非正常RunEnded |
| [idle-read-r1-result.json](../work/game-seek-20261115/idle-read-r1-result.json)、[单组重试](../work/game-seek-20261115/idle-read-r2-gameplay-result.json)、[旧receipt](../artifacts/delivery-20261116/idle-read-preview-receipt.json) | 旧1240冻结；原六步末项缺fixture输入，172/201、整体1；只补输入后同DLL Gameplay201/201、0。旧ZIP37,308,123B，SHA `a7a8af240bb151caea59f14afc50ed7cdf6dff9eac37573b6417ebf4c4dd9571` | 原失败保留；当时继承supporting旧三库机制证据，不能写成旧六步全0或本轮通过 |
| [supporting-read-r1-game-result.json](../work/game-seek-20261115/supporting-read-r1-game-result.json)及stack/exit | 旧3056首次Clock19.0731920秒、接管210.5761087秒返回WaitingForPause；之后idle每tick仍约21–28秒；Pause/Escape各一次无接受回执 | ActualSource.ReadDsp→capture VerifyCurrent/Group阻塞；正常关闭超过60秒无响应，核对PID/path/StartTime后22:04:22.4264633精确Stop，非normalRun |
| [supporting-read-r1-result.json](../work/game-seek-20261115/supporting-read-r1-result.json)、[旧focused138](../work/game-seek-20261115/focused-owned-supporting/checks-r1/result.json)、[旧traversal204](../work/game-seek-20261115/supporting-read-traversal-r1/result.json) | 旧1239冻结、13Runtime+3Adapter限定改动；11步全0；旧138每mode23/23+3pair，旧204每child12/12 | 仅对应supporting DLL；不代替noeffect或receiver各自的新运行，均不授正式Adoption或游戏验收 |
| [group-read-r1-game-result.json](../work/game-seek-20261115/group-read-r1-game-result.json)、[after-exit](../work/game-seek-20261115/group-read-r1-after-exit-observation.json) | 旧13420首次Clock29.4589574秒、接管365.6946435秒返回WaitingForPause；正常Alt+F4后进程/窗口消失，未强制结束 | 无OS退出码或RunEnded；旧traversal-r2共204、每child12/12；r1缺 `StockMethodPause` 退出1原件保留 |
| `source-read-r1-before-exit-toolbox.log`、`source-read-r1-source-a-stack.log`、`source-read-r1-exit.json` | 旧42604首Clock40.806877秒，随后TryIssue→整组Compose接管未返回；正常关闭无效后精确结束 | 未完成会话，不借给后续候选 |
| `clock-read-comparison.json`、`clock-read-r1-before-exit-toolbox.log`、`clock-read-r1-exit.json` | 更早native-coroutine/clock-read首Clock各294.3280863/50.2224856秒；clock-read来源接管无响应，最终精确结束41872 | 各为单条观察，非延迟分布；`CompleteRunVerified=false` |

早期首差依次由 `3A2410` Pause推进至 `3C0310` 普通判定、`3C1310` Retry、`3E8030` NativeJudge及 `402670` Hold.MoveNext。相关 `seal-diagnostic`、`late-basic`、`late-prefix`、`late-retry`、`native-judge`、`native-coroutine` 原日志及结果继续保留。语义读取走通后，实际source接管的长阻塞仍需独立解决。`observe_startup.py` 合并保留的轮转日志后只截最新启动，各候选按独立session关联。

## 已接入的实现与边界

`ExactHookBroker.ReferenceCodeIdentity.cs` 保存真实安装前64B，并证明Dobby source branch、replacement delegate、relay、original trampoline的完整指令搬运与continuation；成功安装并完整证明后才进入私有append-only登记。`ExactHookBroker.Playback.Group.cs` 在同Gate固定成员和generation，先对未组合的固定文件+DIR64 canonical核对全部preimage与写区间，再只组合真正改动的5/6B。原跳板及所属Entry持续重新验证。

`NativePlaybackExecutableSeal.cs` 从独立原文件重建整个执行节并逐字节比较；只读段、头部及helper/Dobby原字节seal保留。相邻Pause/Resume的64B证据可重叠，实际branch写区间不可重叠。

`source-read` 在单次锁定Group验证内显式传递私有foundation作用域，入口/出口完整验证producer/header/ranges；作用域绑定group、thread、token/epoch及generation。`group-read` 的显式 `VerificationTraversal` 只在单次双锁内聚合targets与foundation的完整入口/出口验证，不存入group或ambient状态、不跨调用或getter继承；失败仍sticky，null路径仍fresh。

`supporting-read` 沿同一显式票核对targets/stock/lower/engine/lower-time固定引用和parent；各成员自己的file/cache/metadata/functions、source/body/tail、relocation及unwind仍逐次检查。Adapter reservation登记真实Adoption并完整验证后同次返回adoption/binding/origin；读生命周期不授source或Clock authority，独立accessor/native操作及首尾graph继续fresh，七根仍等实际sample退栈再drain。

`idle-read` 仅在WaitingForPause、同真实run且无session/过渡/fault等状态时，先执行原PumpCompleted及fault/drain，再查询纯dirty信号；null保持NotReady，真实候选仍走full profile。其旧实际会话接管已返回，但发生leaf mismatch并Withdrawn，不能宣称首次Pause通过。

`noeffect-read` 的 `NativePlaybackStockOccurrenceProducer` 保留no-op的原拒绝诊断；private decline绑定原root、completion和拒绝对象，要求normal零语义/物理child、无prior rejection、无其他writer及健康fences，且只在当前真实Returned回调可选中。新Before、Fail、release和Dispose会清除marker；其中Before清除只经静审，不作动态独立通过声明。`NativePlaybackStockRootedSource` 只在fresh且 `_acceptedReadback=false` 时调用 `fresh.VerifySource(this)` 后继续等候；接受首个readback后不再走该等待例外，其他失败保持Fail。

| 实际普通入口 | RVA | 完整前导 / 长度 |
| --- | --- | --- |
| 普通判定 | `3C0310` | `4056574881ECD8000000` / 10B |
| ControllerLevelOver | `3C0C00` | `40574883EC30` / 6B |
| Back request | `3C3A50` | `4883EC2880B9B400000000` / 11B |
| Back terminal | `3C3E20` | `405557488D6C24C8` / 8B |
| Skip terminal | `3C17D0` | `488B01C6415101` / 7B |
| Controller/Result Retry | `3C1310` / `3CFF20` | `40574883EC20` / 6B |
| Result Start | `3D0B20` | `40534883EC60` / 6B |

以上入口来自实际日志，完整指令无PC相对寻址或控制转移；RVA仅定位证据，原跳板仍须等于完整前导并跳回精确续接。NativeJudge/NativeCoroutine只从真实Prepare/Configure/Commit发行私有身份，持续核对具体bridge、原跳板、配置与闭包；变化后sticky拒绝。Hold的有限6B前导为 `488974242055`，未新增RVA放行。当前native helper SHA256为 `d099ae13726fd0ac94abf906506a99190de2260da704e6f91bea27b2e5164823`。

完整七根Seek、HoldEpoch、音频时间精度、Speed/Seek正向及其余B项继续按checkpoint推进；配套controls SDK公开合同未改。

