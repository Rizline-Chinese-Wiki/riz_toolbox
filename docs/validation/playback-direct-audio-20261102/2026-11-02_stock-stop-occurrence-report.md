# 正常重播／恢复的 Stop 子调用

2026-11-02 21:56更新；`flavor=null`。原件位于 `D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/`（T），增量已精确合主16文件，当前894输入／482产品；改前逐文件备份保留，README原有改动未改。native-r3包括七种透明转发的TLS屏障；新SDK和映像仍待终态，整体未完成，n2结果只保留其冻结范围。

## 问题和证据

固定853d游戏副本中，Replay在RVA `3FBF1C`调用`AudioSource.Stop()`，返回`3FBF21`；Resume在`3FC180`调用，返回`3FC185`。既有625837自有fixture直接重置Playing／Time，省略了这两个真实CALL。因此它的normal成功没有执行新direct Stop保护与正常stock的交互。

T新fixture604ebb加入原生Stop CALL，独立profile actual354检查验证19个完整本体、真实RF／UW、十个确切CALL及原有六direct ABI／SEH。没有调用真实游戏代码。基线产品只更换该owned fixture的固定身份和原八CALL常量，未更改native helper或产品行为；`counterexample-r1`两实际进程均exit1：Stop原函数确实执行，Clock却从Paused变Withdrawn，正常后续调度失去权限。结果的passed表示反例确认，不能当两个产品用例通过。

## 实现

保留SourceV4／168B frame／136B info与WatchV2／120B，追加正交StockStop协议V1和frame flag128。Broker只从固定映像或独立已审查fixture的两个精确E8→AudioStop CALL、Replay／Resume MethodInfo配置同一native owner，且只能配置一次。

native在推进direct专用fence前核验当前实际TLS父scope：同owner、owner线程、顶层Replay／Resume、原函数窗口、无callback重入、无先前child或fence推进；exact player/source匹配active七根watch，class、图和source fence仍current，incoming MI为空，caller精确对应父CALL。只有第一次符合的Stop签flag；它仍进入正常Scheduling、journal、Before／Returned和真实PhysicalExit责任。该flag不独立发行Clock、schedule、writer或generation。

Router仅将native签出的该Stop交给原stock producer。Producer要求Replay Stop→pitch→schedule，Resume Stop→time→pitch→schedule，每个child均核同source、同parent、调用位置、次序、正常返回及实际退出。外部Stop和其他直接播放／循环入口仍结束旧权限，不能用公开地址、相等frame或caller的完成bool跳过。

## 实际范围

| Evidence | 结果 | 结论 |
|---|---|---|
| T `profile-r1/test-result.json`、`profile.json` | actual354、19本体、10CALL | 新604ebb固定fixture身份独立确认 |
| T `counterexample-r1/test-result.json` | 两实际exit1，反例确认 | 旧保护误撤正常stock Stop |
| T `native-r1/native-build-result.json` | `/W4 /WX` build0 | 首版StockStop私有协议 |
| T `positive-r1/test-result.json` | fresh build0/0、两actual0／20 | 首版实际19Dobby组、嵌套Stop、Clock Scheduled、GetTime发布保留 |
| T `native-r2/native-build-result.json` | `/W4 /WX` build0 | 加active watch.Source当前性 |
| T `native-checks-r3/test-result.json` | 17进程／792，全0 | 正常、错MI／CALL／父、callback／nested重入、重复、worker、SEH、class、stale／ABA反例 |

native首测试fixture误将target和original填成同一地址，r1的14个实际失败保留；r2改为独立target markers后14／631通过。随后产品增加watch.Source当前性并补stale／ABA／nested三种模式，r3得到17／792。没有为测试设置错误改变产品Configure约束。

当前`positive-r2`使用新native-r2和fresh managed snapshot跑九个定向模式；正式SDK normal、同／跨局后正常Stop、固定映像与新符号／整树仍需终态，旧Q的625837／2598不能签此新行为。真实SDK selected rate恢复必须实际执行，不能只用rate1的两正向代替。

21:45实际补充：`positive-r2`已9进程200全0，新managed整树28套2598、solution零警告零错误、SDK14、六Release全部actual0。随后独审实证n2的一处TLS误借，因此当前联合验收改用n3。n3与foundation的482产品输入逐项相同，仅native CPP和runner后续变化；不因native-only变化重跑无影响托管矩阵。

独立反例位于相邻D目录`playback-stock-stop-occurrence-r1-20261102-independent-review/foreign-player-shared-r1/`。outer current Replay原函数内透传foreign Replay，共用current source，外层随后直接返回；foreign root无journal，却使Stop得到flags129及outer ParentSequence。反例实际exit1，SHA `d6e6cee91aeaa9c9194f5c22c76bf8568df488f128e35adb12ae8920abe41400`。n3的`ForwardIgnored`覆盖七种ignored-original路径：临时清空TLS，在原函数正常或SEH退出后finally恢复；相关current-source leaf因此成为独立parent0干扰，不能借outer stock。原参数和unsigned rawAL继续原样传递。

n3实际已通过`native-checks-r4`17／792、`original-eight-r3`14／36675及`native-regressions-r2`四范围94／15556、27／7767、20／6735、15／4622；`native-symbols-r3`4对原生PE/PDB＋12linked COFF／173源码记录、`symbols-r2`5对托管PE/PDB／514文档均匹配。`positive-r3`、`formal-r2`及`mapped-r2`正在新native上运行；独立三代理补foreign原反例复验、handoff后真实Stop、七ABI／SEH恢复。未把这些pending项目或旧n2结果写作n3整体通过。

21:51补充：`positive-r3`已实际9进程200全0，包括正常带Stop的Replay／Resume、19成员native／Runtime两路径、foreign／worker、Progress四／七根及owner-pump21。当前[已完成范围索引](stock-stop-completed-scopes-r1/scope-index.json)实际核对14份结果和2283个引用文件；索引passed只签原件完整性，formal／ABI／整体完成仍false。`candidate-review-r1`已冻结894输入、16个相对主目录变更、482产品输入（475cs＋6项目＋1JSON），尚未合主。

21:56实际补充：独立审查包`foreign-player-shared-r2/positive-r3`新EXE的六进程328全actual0：正常Replay／Resume、foreign shared source的Stop flag1/parent0、foreign player原SEH `e2440012`、foreign Start的raw AL165及SEH `e2440013`。每次foreign正常／异常退出后，outer合法Stop仍flag129/parent1。同旧EXE的n3对照原exit1保留，它唯一剩余失败是旧“parent必须等outer”的证明断言，未伪造成功。独立七ABI完整矩阵和n3 source-open后Stop仍分别待其实际终态。

T/main-apply-r1实际passed，894逐项匹配，16个文件已应用；功能整体、完整formal矩阵、生产和全B状态仍false。T/main-verification-r1正在从实际主目录重新冻结、构建、执行九项200并核514文档，终态另录。当前n2 mapped-r1已108全0，只签n2；n3 mapped-r2仍实际运行。

完整[独立审查报告](stock-stop-independent-review-r1/2026-11-02_review-StockStopV1-report.md)及118份附件已按清单逐项核对并原样保留在本项目内，合计约2.77MB。它包含旧失败、同EXE对照、新6／328、scope、原始命令／PID／exit、源码快照及固定映像纯文件证据；不要求读者依赖本报告的转述。

22:06主目录fresh验证已终态：build零警告零错误、九进程200全actual0，实际5PE/PDB514及894输入逐项匹配；T/main-verification-r1结果SHA `9e9b8f7fae31595347483b2e76f260322594d8736e1a988b5cc628bf2efb0607`。完整formal／mapped／七ABI／handoff仍待各自终态，未重复已完主目录fresh或借n2代验。

22:17其余终态已收：n3 mapped-r2 metadata69＋joint39＝108全actual0；[同／跨局Stop补测](stock-stop-handoff-r2/completion-evidence.json)同局89＋跨局85＝174全0，8个真实Stop和8个reserved restorer、cleanup前后及根0，只签rate1，零SDK值事务。独立测试895输入的5对PE/PDB／515文档已核；产品四DLL及PDB与当前产品对应。

[七ABI完整报告](stock-stop-ignored-seven-abi-r1/2026-11-02_ignored-seven-abi-report.md)分列r1主线程136／16660和r2 worker28／1834的实际通过；首轮aggregate中14个fixture错误Flags断言保留，r2只修Unknown|Dormant预期，不改产品。raw Bool1／AL 00/01/80/ff、float/double q/sNaN、foreign无journal、相关leaf parent0、SEH后outer恢复、worker责任均按各自scope有证据。其3对PE/PDB、6COFF、84C13源记录匹配，1012附件已回核保留。

当前[整合进度索引](stock-stop-integration-progress-r1/scope-index.json)实际回核21份完成结果和2421个引用文件。唯一当前增量未终态项为T/formal-r2完整SDK18模式；n3 normal／first／same仍live，其他15模式排队。索引passed只证明已完成范围的完整性，功能／完整SDK／生产／全B完成状态均未签。源与所有前置失败保持，不再重跑已终态范围。

## Evidence → Finding → Path

F-STOP-1（validated，high，n/a_re）：E-STATIC固定两个游戏CALL＋E-COUNTER两失败证明旧fixture遗漏一个会触发保护的实际步骤。路径P-STOP：实际Replay／Resume父scope → Stop Before → 旧direct撤权 → 后续schedule被拒。

F-STOP-2（实现子范围validated，high，n/a_re）：E-NATIVE17／792与E-POSITIVE首版20证明可以按实际父窗口和固定CALL区分唯一正常Stop；完整SDK及新组合仍pending。修复路径：闭合profile → native owner一次配置 → 同root首child attestation → 原producer完整次序与物理退出 → 原Clock／provider读取。没有扩大生产准入。

完整合同在T `IMPLEMENTATION-CONTRACT.md`，每个实际命令、PID、exit和原始日志在对应scope目录。续接先看 [CURRENT-CHECKPOINT](../../../.docs/CURRENT-CHECKPOINT.md)。禁止游戏安装／启动／UI／账号；未知lower、完整generation、production、任意Seek／effect及B01–B19仍未完成。
