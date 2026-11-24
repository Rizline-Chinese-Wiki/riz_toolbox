# 当前续接点（2026-11-16，优先读本页）

## 当前：clock-read-r1已安装，待本新会话实测

`artifacts/delivery-20261116/RizToolbox-0.3.0-clock-read-preview-20261116.zip`，37,303,018B，SHA`839205a3f36fe2d93da3eda9102de8e1194d30b19fb66824823760e523d31596`，35成员读回、25文件安装匹配（9368自然0/7ff9d5，`install-clock-read-observation.json`）。14:32:02官方Steam已请求启动，新PID待读。helper/Runtime/Core/SDK逐字节未改，沿用native-coroutine，不重复原生矩阵。**新响应/真实Speed/Seek未验收。**

读取consumer scope实现已冻结：Read/ConvertObserved/ConvertTime各显式作用域，辅助字段/Mapping/转换共用当次懒取freshprofile，失败也仅一次，嵌套新consumer独立刷新、finally恢复且清除，不跨Read接受缓存。RequireMapping独立参数保留Seek准备，SetSpeed原独立Refresh保留；真正native converter仍原前后profile验证。root额外核调用链后删除ReadPlaybackClock里丢弃结果的chart Refresh：native reader本身VerifyCurrent→session.ReadControlledClock，原source/run/graph/readback全部保留。新增聚焦检查1组覆盖真实service多属性/下一Read、nested/异常清理/失败恢复。

本版`clock-read-r1-result.json`九步全0（33188自然0/178d37）：fresh owned fixture、Loader六组件/Gameplay/Examples/Hooks四Release、Hooks210、clock-profile14、host3、默认Gameplay201/201。scope新源码不能用前包294秒日志验通过，下一只对本新包核首个实际Clock耗时及Host CapturingSource后阶段。当前无构建/代理修改在途，勿再次启动前一个卡包。

## 最新实测：镜像冲突已跨过，294秒重复Clock刷新阻塞，正在修复消费作用域

native-coroutine实际10856已进入同曲FREE，run`83a8f61a291841acac50cef435060585`/session`aab0bbb64d7c41aeac6ec2a013ee24ff`。本新会话无普通/协程composition警告、无firstDiff或group-lost；06:18:30 UTC首个Level `GAME CLOCK`返回，GameTime=Chart单位、Real/Synced=秒单位，来源语义已识别。**这只证明新来源读取实际走通，Speed/Seek未通过**。该Read从monotonic163.1152784到457.4433647，耗294.3280863秒；随后06:18:38 RunStarted，06:18:57 Host CapturingSource。新真实卡点是读取内部重复Refresh，并非新未知写区间。

两份新只读栈`native-coroutine-r1-clock-a-stack.log`/`-clock-b-stack.log`显示同一次ModuleRuntimeProbe Snapshot→GameplayService.Read，先在ClockSourceIdentity、后在ReadPlaybackClock中重复CaptureGetTimeHook→CodeProfile/whole group/foundation ReadRanges。Sol源码确认完整Read可8次Refresh且Refresh后再重复Verify。已发 `coroutine_owner_impl`实施真正顶层Read/转换显式scope、内部懒取一次freshprofile、finally清理、嵌套consumer独立票；真实native conversion/reader原前后检查、source/run/图变化、域外fresh/Prepare/Seek权限仍独立，禁止跨Read接受缓存。**该新Adapter代码尚未编译/测试，不借本native-coroutine旧执行包验它。** root计划新cold式托管聚焦矩阵，不重编未改native或重复原生长矩阵。

CUA Alt+F4退出请求后仍长时间Responding=false，14:23:10已严格核路径/StartTime/PID后仅Stop-Process10856，CUA观察窗口消失。不是正常退出或完整run结束；证据`native-coroutine-r1-exit.json`。退出前最新25文件核对观察`native-coroutine-r1-before-exit`及完整日志保留。不要重复启动这一已知卡包；等scope修复构建后换唯一新包。

## 当前：native-coroutine-r1已安装，正在新会话初始化

新ZIP `artifacts/delivery-20261116/RizToolbox-0.3.0-native-coroutine-preview-20261116.zip`，37,301,600B，SHA`78a434ef64513671876ab4d80fd85e88765da87cdaec1e82010be6ef9333c608`，35成员逐一读回；25文件安装匹配（50238自然0/92f65e，`install-native-coroutine-observation.json`）。新helper88导出，SHA`d099ae13726fd0ac94abf906506a99190de2260da704e6f91bea27b2e5164823`。14:07:04 Steam请求启动，实际新进程待读；不签游戏/Speed/Seek。

产品已接入真正Coroutine private owner、Prepare original完整指令/continuation、实际5/6B branch和relay，native只读配置回执绑定kind/slot/entry/original/callback/User/config，真实Installed后发布；bodyHash仅还原branch仍核全正文，unknown prefix只log不授composition且保留原调用。新Release/Hooks210及88导出、Judge两原生模式、Coroutine三role配置回读278条运行CHECK全0；产品六步构建exec3467自然0/be51b5。自有typed检查由root补完（原harness代理未落盘），新`native-coroutine-owned-r1`九步自然0/23f919（2402）：普通四模式+真正NativeJudge+Coroutine MoveNext/Factory及独立body-tail负向模式全过。后两role的native原调用/返回/context各1、detach、配置回读/私有snapshot/整.text/完整bodyhash和branch或tail篡改sticky都已实际执行；不执行GameAssembly/Unity。打包核实际执行的helper/Runtime/Core/SDK与包一致。独立Sol只读审查无阻塞，三代理均已冻结，当前无编译在途。

下一等本新会话初始化，`observe_startup.py native-coroutine <unique-tag>`核25文件并归档；CUA新列窗口→菜单→同曲FREE开局，读第一实际首差/clock profile。不得复用native-judge旧游戏验本新源码，不重复旧矩阵。

## 最新：native-judge真实首差已跨过，正在实施Coroutine

PID44860已完成启动并实际进入GreatnessEarth Axya EASY4 FREE，run`74104c602d2f463681686204d2cec775`/session`83d7b0fe28a34bed8c424b802b890e67`。05:41:29 UTC首差推进到Hold.MoveNext@402670，expected `488974242055574154488D6C24B94881`，actual `FF2562DB1681574154488D6C24B94881`。NativeJudge首差3E8030已跨过；Speed/Seek仍sticky拒绝，整体未通过。25文件核对后的`native-judge-r1-final`观察及完整日志已归档。44860经CUA Alt+F4正常退出，窗口消失；未强杀。六个相关当前源冻结在`work/game-seek-20261115/native-judge-r1-frozen-source/`，完整执行输入仍以native-judge ZIP为准。

当前3位新Sol/max正分工：`coroutine_owner_impl`只改产品Coroutine私有owner/实际原跳板及配置回读；`coroutine_owned_harness`只改work自有MASM/harness，加真正MoveNext/Factory桥组合正反向；`coroutine_config_fixture`只新增纯自有C++配置回读检查与运行脚本。产品新增有限完整6B前导`488974242055`，同实际Prepare/Commit+原配置回读，不RVA白名单/live rebaseline/跨调用cache。新只读ABI`RizCoroutineContext_ReadConfigurationV1(kind,slot,out)`，0x48六uint+entry+0x28config，0成功/1非法/2未configured或中途变化，导出预计88。尚未构建/执行；旧210、owned和本游戏结果均不签新Coroutine源码。root统一等各源冻结后serial native/fixture/Release/Hooks/owned检查；当前无编译在途。下一先收三代理产物、跑新验证再打唯一新包，不能重复native-judge游戏。

## 当前安装与实际进程

`artifacts/delivery-20261116/RizToolbox-0.3.0-native-judge-preview-20261116.zip`：37,299,721B，SHA `69c3bf74c18635806a5bc897bc908aeb05e0b12953ff23ad0d06063dedc9ce5c`。35成员逐一读回；25运行文件已安装匹配（exec67746自然0/73977c），见 `work/game-seek-20261115/install-native-judge-observation.json`。新helper有87导出，SHA `dff66c226f2141007848ca8457e3eea442ac6911772cb540691039fb62925397`；必须与新Runtime成套，不能复用旧helper。

13:36:06经官方Steam 2272590请求启动，实际新PID44860，13:36:08启动；最新05:37:32 UTC source prepare ProgressStartMoveNext，仍初始化，尚未签本候选游戏通过。旧游戏44372已CUA Alt+F4正常退出、窗口消失，未强杀。当前没有编译在途，3位Sol/max实现/fixture/独立只读审查均已完成冻结。

## 本批已完成的实现

1. group执行区保留最初页身份，每次仍由固定原文件+DIR64+已证真实patch重建、逐字节比较整个执行节。头部/只读段及普通Dobby/helper seal保留，不对当前live重新建基线。
2. 普通broker的真实Dobby安装才可发布私有成员；同Gate固定generation snapshot，全部独立canonical preimage先核，再只组合真实5/6B branch。原跳板完整搬运指令及精确continuation、delegate/entry/metadata/relay/页身份持续检查。已补实际日志七种完整PC-independent入口；没有RVA白名单或任意decoder。
3. **NativeJudge已接入**：真正Prepare前捕获原preimage、保留实际Prepare original、完整prefix与continuation证明，Configure之后再核，Commit成功且branch/relay读回证明后才发布。Entry.Original不伪造Delegate，native owner保持真正Entry/bridge/engine绑定，复用同一私有membership/snapshot。
4. 新只读`RizJudgeContext_ReadConfigurationV1`返回实际configured/enabled/entry/original/observer/user/原配置（0x48）。具体bridge私有方法核其原始Configure参数、托管闭包/delegate/分配及真实原生回读，差异令bridge和owner sticky失效；helper实际映像与导出持续核验。未扩大到独立Coroutine。

## 当前版本新验证（不借旧包验新源码）

- `native-judge-r1-result.json`五步自然0，原exec29579终态b96bcb：新native helper及两C++模式、Loader六组件Release、Examples、Hooks、Hooks210/210。实际导出检查87通过。
- native两种纯自有ABI/publication模式分别31308、61405个重复运行时CHECK，全部通过；**不是这么多独立测试**。新增47条配置回读断言已包含。保留SEH/C++异常、GPR/XMM/参数、disabled原调用与四线程等既有范围。不加载/执行GameAssembly或Unity生命周期。
- `native-judge-owned-r1/result.json`七步全0，原exec16012终态d785d3：新MASM、新managed harness、普通normal/tamper-branch/tamper-owner/foreign-preimage及真正native-judge模式。normal七类prefix仍全过；native模式23项标签，真正Dobby+具体bridge的native原调用和before/after/context dispose各1、native与普通共10私有成员的整.text组合、detach后原调用、branch篡改及恢复后sticky拒绝均通过。
- native模式原preparedOriginal==真实helper回读original；不是CLR fake issuer或constructed stock group。scope只自有机制，stock/production/game权限均未提升。打包前已核这些结果、当前Runtime/Core/SDK执行字节和新helper SHA与包输入一致；35成员读回。
- 独立Sol只读审查未发现阻塞bug。主README SHA仍 `CFBAC4691D36EDC5DABA513AA62D5856AE61E29BF553D0DC2979832750847364`。新SDK公开合同未改，继续配套11/15 controls SDK ZIP（SHA `4014745bc6e54dcee4e159857c2cad824834030b7f218dd6f942e2f74f37ca6f`）。

## 最近真实游戏证据与下一步

旧seal首差3A2410(Pause)→late-basic首差3C0310(普通判定)→late-prefix首差3C1310(Retry)→late-retry首差3E8030(独立NativeJudge)。完整late-retry会话普通composition identity警告0；run `936f0bb379284922ab5e2a1d1600a8e7`，05:18:47 UTC expected `48895C2408574883EC30803DA8F8BB01` / actual `FF25B28118814883EC30803DA8F8BB01`。这才是当前NativeJudge接线的实测依据；旧Clock组已sticky失效，旧Speed/Seek拒绝，不借此签新包。

1. 读44860最新阶段至Unity host完成；`python -B work/game-seek-20261115/observe_startup.py native-judge <unique-tag>`会核25安装文件、合并轮转日志并仅截最新真实会话，标签唯一。
2. CUA重新列窗口，选择实际新Rizline窗口；进主菜单/选曲/GreatnessEarth Axya EASY4 FREE，触发本候选Clock profile。记录首次RVA或成功。不要复用旧窗口6227466或旧坐标/截图。
3. 若新首差是独立Coroutine，按实际永久owner/原跳板/真正配置继续私有接线，不接受未知改写。若group可用，继续完整七根Seek/HoldEpoch、实际DSP/音频时间精度、Speed/Seek正向及其余B01–B19/engine功能。整体goal active，未完成。
4. 不重做相机/N1/退休批/rate/float/Play移交或重复未改长矩阵，不用旧fixture/旧ZIP结果验新源码。当前验证来源见本页，源/结果原件都保留。

## 工作约束与历史

用户要求功能优先、性能后置、按最新结论，不做旧兼容；必要游戏退出/安装/实测已明确授权，旧Escape后已选择“允许恢复实测”，无新停止时不重复问。禁止过期账号/认证UI；不commit/push。代理只用Sol/max，构建串行BelowNormal，避免占满电脑。

旧页逐字保留在 [本次更新前历史](history/20261116-before-native-judge-checkpoint/CURRENT-CHECKPOINT.md)，更早长历史见 `history/20261116-before-cold-runtime-refresh/`。旧ZIP未改；四个late-retry composition源已冻结在 `work/game-seek-20261115/late-retry-r1-frozen-source/`。当前源已进入NativeJudge新版，不以该旧源码/旧二进制验新内容。报告 `docs/2026-11-16_late-hook-composition-report.md`。
