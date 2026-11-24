# 底层实现与验收状态

**2026-11-23，Windows Steam 候选为 r18。** 玩家可选纯底层 Runtime 或自带成绩面板、主题和快捷面板的 Basic 版。六份部署 DLL 的 11 套离线回归共通过 2,105 项。自制谱结算和官方成绩隔离、官方谱变速与跳转仍需当前包的 Steam 实测。[下载和验收记录](../.docs/CURRENT-CHECKPOINT.md)

2026-10-30 核对了当时的 Single V7 冻结，正式采集、2,233 项托管回归、32 项验包和 26 项合成安装恢复均通过。[当期进度](../.docs/PROGRESS.md) · [冻结记录](validation/foundation-single-v7-20261029/README.md)

核对日期：2026-10-25（UTC+8）。**尚未全部完成。19 个整项任务中，仅 B03 完整验收；B18a 是已完成的诊断子任务，不代表整个 B18。** 完整标准见 [SPEC](../.docs/foundation-expansion/SPEC.md)，任务勾选见 [CHECKLIST](../.docs/foundation-expansion/CHECKLIST.md)。测试数不代表能力覆盖率。

## 已验证到哪里

<!-- DOORSTOP-COLD-LATEST:BEGIN -->
最新补充：[实际Doorstop冷启动接线已验证](validation/foundation-doorstop-cold-20261025/README.md)。session80976实际exit0：3正常冷启动+2错误部署拒绝、原21转发回归、旧默认Doorstop构建均通过。82名称/ordinal保持，候选真实DllEntry初始化先于自有Importer的TLS/C/C++/DllMain；626源/8二进制/585原件归档。固定游戏四引擎入口profile事实已补全。尚未安装或运行游戏，生产V4/新核只读采用、对象GC、调度、取消与全部资源退休仍待完成；B01–B19状态不变。
<!-- DOORSTOP-COLD-LATEST:END -->

以下为前一批独立转发组件的历史时点；其“尚未接入Doorstop”已由上述候选接续，仍未接入真实游戏。

<!-- WINHTTP-FORWARD-LATEST:BEGIN -->
最新补充：[固定系统 WinHTTP 转发已通过独立宿主验证](validation/foundation-winhttp-forward-20261025/README.md)。root实际session40901/exit0，21进程、5653父核验；613声明源/4二进制/260原件归档。TLS与C/C++入口保留问题已修复，CFG保持。该组件尚未接入生产Doorstop或游戏；下一步是生产首次发布与新核采用，然后对象GC、调度、取消和全部消费者退休。此前1617属于旧完整构建，本批未重跑；游戏实测仍稍后，B01–B19验收状态不变。
<!-- WINHTTP-FORWARD-LATEST:END -->

以下新核启动链为本批之前的历史时点，其中关于WinHTTP尚未实现的描述已由上方接续。

<!-- EARLY-KERNEL-LATEST:BEGIN -->
- **最新原生集成：新 Kernel 自有启动链已接通。** [本批原件](validation/foundation-early-kernel-20261025/README.md)。root 实际观察 session17459 终态0；新链73/13248断言、旧发布器57、旧链73/13031断言均通过，共203个自有宿主；604项声明输入和18条二进制按7+4+7隔离归档。本批没有重跑上一批25套1617，23项包字节保持。
- **实际修复与证据**：独立 publisher 记录2、wire3与helper ABI2/scalar1分层；复用同一核心代码，固定模块/入口/epoch不混用。调试观察改为有界映射身份与线程硬件返回断点；3个错栈经过保留责任、后续真实RSP返回清槽。正式debug为5Load/0Unload，另3次强化预飞实际各6Load/1Unload；1007未命中，不计覆盖。首批31803与失败诊断原件保留。
- **直接下一步**：实现固定系统WinHTTP转发及生产启动凭据，再接新核的CLR只读采用、coordinator/真实对象GC、scheduler/async/Start内联清理和全部消费者资源退休；补完整Retry/Return/Loader。新链仅用于自有宿主，真实游戏仍是旧安装，18安装项/4原始文件hash一致；未安装或运行游戏。B01–B19仍active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。
<!-- EARLY-KERNEL-LATEST:END -->

下方为上一批独立核完整构建的历史时点；其“下一步”已由上方接续，不代表当前固定链仍未实现。

<!-- ENGINE-KERNEL-LATEST:BEGIN -->
- **最新完整构建：独立协程运行核已实现并验证。** [本批证据](validation/foundation-engine-kernel-20261025/README.md)。session23528实际exit0，25套1617/1617，599项声明源/23项包冻结。新Kernel仅39424字节、8个ABI2导出和2项系统导入，无入口/TLS/CRT/Dobby直接依赖；canonical入口表改为typed constexpr，旧helper继续独立回归。
- **实际范围**：新核7进程：128入口与256拒绝、1606宿主检查/1486父断言；原生3402+五托管889检查，180原调用/152配对。旧39早期宿主/7316另计。31项输入在新核运行期间持只读句柄且hash不变；后续另一测试正常复制旧helper的一项变化及准确旧文件恢复已单列。94条去重编译诊断保留，未声称零warning。
- **接下来**：新Kernel尚未由生产或固定早期链采用；下一批独立绑定它，再实现真实WinHTTP/首次发布与V4、coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休。新包未安装，游戏实测稍后。完整B01–B19仍active，仅B03整项/B18a子项验收；EngineCancelled=false、Pending保留、RequestPlay关闭。
- **安装补充已修复并实测**：[独立证据](validation/foundation-kernel-install-20261025/README.md)。新23包在旧22项白名单下的真实拒绝已复现；精确加入Kernel后，自有临时目录16项安全检查和13项恢复检查通过。三脚本覆盖修复与原核心构建分列，现行599源码另有组合归档，其余596项不变；没有再次宣称重跑1617，也未操作真实游戏目录。<!-- KERNEL-INSTALL-SUPPLEMENT -->
<!-- ENGINE-KERNEL-LATEST:END -->

### 上一批固定发布早期链（历史）

<!-- EARLY-FIXED-LATEST:BEGIN -->
- **历史：无 Dobby 的固定发布早期链已接通，73 个独立进程通过。** [本批证据](validation/foundation-early-fixed-20261025/README.md)。root 观察 session34351 实际 exit0，14 项输入/7 个二进制已冻结；全 Prepare→4 Configure→Bind→4 Commit/Ready 早于真实 TLS/C/C++/DllMain 首调用。正常86/worker92次原调用，各56观察配对，包含真实前缀副作用和原异常。
- **本轮修复与验证**：Capture Busy/错误现永久关闭原生准入，真实受控递归及后续捕获均不能误放行；独立未发布进程建立同 binary/RVA 的 SEH/C++ raw flags 基线。73 个进程含56内部故障、实际1字节部分写和17条调试补充观测；API结果注入、跨 child 基线和仪器化边界均单列。上次完整25套1617/1617仍属于99557，本批未重跑托管套件。
- **接下来**：实现实际游戏的系统 WinHTTP 转发与可信首次发布/独立 V4，再接 coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休，补完整 Retry/Return/Loader。游戏实测稍后，本批未安装；B01–B19 继续，仅 B03 整项/B18a 子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。旧576/12声明源、已装18文件/326源ZIP和4项原游戏hash已重新核对。
<!-- EARLY-FIXED-LATEST:END -->

### 上一批独立固定组件（历史）

<!-- FIXED-PUBLISHER-LATEST:BEGIN -->
- **历史：固定四入口最小发布组件已实现，57个独立进程通过。** [本批证据](../docs/validation/foundation-fixed-publisher-20261025/README.md)。12项组件输入/4二进制独立冻结，session5523实际exit0；已去掉该发布路径对Dobby、动态路由、堆和CRT日志的依赖，原函数桩重放真实非NOP前缀。34原调用、20观察配对、16异常捕获；57份实际页快照、2850个控制展开上下文均有独立核对，控制上下文不等于逐PC真实异常。
- **状态分列**：上批完整25套1617/1617与39个早期宿主属于session99557的576源/22包归档；本批未重跑这些托管套件，也未把最小组件接进早期loader。FF25尾部实际展开失败后改R11续接，SEH flags改用未修改真实基线精确对比，失败原件保留。56故障中3类API失败是受控注入，实际partial3字节另有原件。
- **仍缺**：无Dobby早期宿主的同链验证、并发首写/实际游戏启动点、系统WinHTTP转发/独立V4、生产coordinator和对象GC、scheduler/async/Start内联清理、全部消费者资源退休及完整Retry/Return/Loader。新组件未安装；完整B01–B19 active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。游戏实测稍后。
<!-- FIXED-PUBLISHER-LATEST:END -->

### 历史完整构建：旧早期宿主

<!-- EARLY-LOADER-LATEST:BEGIN -->
- **最新：自有早期加载宿主已实现并纳入完整构建。** [本批结果](../docs/validation/foundation-early-loader-20261025/README.md)。25套1617/1617离线回归；另39个独立子进程、7316条父断言通过，两组计数分列。576项声明源码/22项包冻结，新包未安装。
- **本批证据**：真实静态依赖链，四目标发布早于Importer的TLS/C/C++/DllMain；normal86与worker92次原调用，实际嵌套/worker/EAX/原生异常及加载后Attach；迟到已有worker拒绝和24处API边界故障已验。额外debugger子进程观察helper/Dobby真实初始化返回，明确属于内存断点仪器化证据。原件合计324次原调用/168观察配对/24异常捕获，完整构建session99557实际exit0，保留94条去重原始编译诊断。
- **仍缺**：游戏的生产早期安装、系统WinHTTP转发及独立V4凭据、coordinator实际对象/GC接线、scheduler/async/Start内联清理和全消费者资源退休。Dobby的CRT堆、日志及未解间接路径仍不具备生产loader安全证明，下一段实现固定四入口最小发布器。若干拒绝项是受控expected/selector条件，24故障不等于Dobby内部OOM/写入中断。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19继续，仅B03整项/B18a子项验收。游戏实测稍后；旧1021安装18项/326旧源ZIP与原游戏3hash匹配。
<!-- EARLY-LOADER-LATEST:END -->

### 历史1617（返回ABI与协程身份）


- **历史：返回 ABI 与协程身份关联已验证，25 套 1617/1617 离线通过。** [本批说明](../docs/validation/foundation-engine-binding-20261025/README.md)。修正 Execute 保留完整 uint32 EAX，返回 ABI2 拒绝采用旧 helper；Start/Release 仍保 AL。现有三类 tracker 的 receiver/slot/generation/Enumerator/延后 Coroutine handle 与 native owner、node/engine generation 分别关联，foreign 隔离/地址复用/有界溢出保留责任；Runtime177、Audio640、Hooks189，565 源/22 包冻结未安装。
- **实证范围**：自有原生3402检查、136原调用/124观察配对；真实 Dobby→CLR→实际 identity/node账本五进程889检查、44原调用/28context，scalar provider/pairs受控。13组身份回归中3组走三类实际tracker实现及受控backend，与Runtime正式字段投影7组synthetic metadata回归分开证明。full51261 exit0；94条去重原始编译诊断，未声称零warning或干净换机复现。交叉复核发现的foreign遮蔽下owned未知node漏记已修正，修正前48324仅保历史原件。
- **仍缺**：UnityPlayer四入口的生产安全首次发布/独立V4凭据和coordinator接线；调度器消费/销毁、async与Start内联清理、实际对象/GC、全部writer/delegate/future消费者、完整资源退休及Retry/Return/Loader。新研究实际找到独立责任，不能把pre-il2cpp_init或Release0当完成证明。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19仍active，仅B03整项/B18a子项勾。游戏实测暂缓，旧1021安装18项/其326源码ZIP与原游戏3hash匹配。


### 历史1597（节点观察与标量账本）


- **历史：引擎协程节点观察基础已实现，25 套 1597/1597 离线通过。** [本批说明](../docs/validation/foundation-engine-node-20261025/README.md)。四种实际 native ABI 的专用传输、只读 scalar 桥/多观察者分发及 Adapter 节点代次账本已接；关联同步首执行，分别记录摘链、执行窗口和最后 Release，After 不回读可能已释放的节点。Runtime170、Audio627、Hooks189；561 源/22 包冻结未安装。
- **本批实证**：自有目标真实 Dobby 原生检查3191项（128 original、116观察、4旧call跨Attach、5释放node情形）；真实 Dobby→新CLR→实际账本五进程855检查/44原调用/28配对context。另增10组dispatcher合同与8组ledger回归。full11589 exit0，保留 111 条不同原始编译诊断，初次环境遗漏和测试编译失败日志保留。
- **仍缺**：四个 UnityPlayer 入口的独立安全首次发布/V4 双映像凭据及生产 coordinator/tracker 接线，实际引擎 owner/Enumerator/Coroutine关联、scheduler撤销和Start内联清理、全部writer/delegate leaves/future消费者封口、完整资源退休与Retry/Return/Loader。自有scalar/返回受控，未执行游戏；FinalCleanupObserved不等于EngineCancelled。EngineCancelled=false、Pending保留、RequestPlay关闭，B01–B19 active，仅B03整项/B18a子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1579（操作入口预初始化）


- **历史：18 个新增操作入口已改为启动前统一安装，25 套 1579/1579 离线通过。** [本批说明](../docs/validation/foundation-operation-preinit-20261024/README.md)。12 个监听变化、6 个 Stop 与既有入口统一准备并发布 27 处；生产 CLR 严格要求完整 V3 身份，只 Attach，不在游戏初始化后补装。这解决源码中的晚安装路径，真实游戏冷启动仍待验。546 源/22 包冻结未安装，Hooks189、Audio619、Runtime160。
- **本批实证**：固定游戏映像六进程 5074 检查，全部 18 tuple 与篡改拒绝；同映像生产 CLR 只读采用 384 检查/两进程。自有真实 Dobby 新传输 1477 检查（84 原调用、含跨 Attach 旧调用），新桥→生产 broker 五进程 837 检查/75 原调用。元数据受控，两类测试不拼成真实游戏链。full94416 exit0，保留 111 条去重原始编译诊断。
- **仍缺**：真实冷启动和 18 入口活体 metadata/调用、并发首发及任意部分发布故障动态验证；引擎 node 登记/执行/最终清理（已找到 UnityPlayer 静态链）、全部 writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整 Retry/Return/Loader。EngineCancelled=false、Pending 保留、RequestPlay 关闭。B01–B19 active，仅 B03 整项/B18a 子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1575（取消请求与退休屏障）


- **历史：取消请求与退休屏障已接入，25套1575/1575离线通过。** [本批说明](../docs/validation/foundation-cancellation-request-20261024/README.md)。六个Stop声明typed观察关联现有owner/receiver/enum/handle/代次；独立记录请求、原函数返回和异常。共享Pending在provider前发布，阻Ready/Commit及三类协程后续根回收；不生成引擎取消完成。Audio619、Hooks185、Runtime160；534源/22包冻结未安装。
- **本批实证**：自有Dobby实际Prepare/Configure/Commit 1165检查、69观察+1未发布原调用；真实native→CLR→实际mutation/cancellation账本五进程544检查。图/根/provider/初始idle受控，实际引擎Stop与IL2CPP未执行；不是游戏并发首写安全证明。新增8组请求、6组Ready发布、8组退休屏障；full45961 exit0，保留94条去重原始编译诊断。
- **仍缺**：18个新增操作入口的真实metadata/安全首发与冷启动、scheduler登记撤销和取消完成、全部writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整Retry/Return/Loader。EngineCancelled=false、Pending保留、RequestPlay关闭。完整B01–B19 active，仅B03整项/B18a子项勾选；实测稍后。旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1553（监听变更与取消身份）


- **历史：监听操作观察与协程身份已接入，25套1553/1553离线通过。** [本批说明](../docs/validation/foundation-listener-mutation-20261024/README.md)。生产源码接12个固定操作入口；添加后移除恢复同图仍记录独立代次。实际Coroutine句柄与Enumerator/owner/receiver/槽代次关联。Hooks185、Audio597、Runtime160；522源/22包冻结未安装。
- **实证分层**：native Operation1156检查/72原调用；真实native→CLR bridge/broker五进程259检查/18原调用，均为自有direct-entry目标，无Dobby首写/游戏证明。10组生产CaptureDispatch根采用故障、8组实际mutation账本、6组协程身份与8组broker回归分别通过；账本图/根仍受控，Operation CLR没有串实际ledger。full60161 exit0，保留94条去重原始编译诊断。
- **仍缺**：新操作入口真实metadata/安全首发及游戏冷启动、序列化/泛型/直接写者和全部delegate leaves、Stop请求与取消完成、全部future和消费者资源退休、完整Retry/Return/Loader。MutationClosed=false、Pending保留，RequestPlay关闭；完整B01–B19 active，仅B03整项/B18a子项勾选。实测稍后，旧1021安装18项/其326源归档与原游戏3文件仍匹配。


### 历史1521（监听回调与账本）


- **历史：listener 回调与账本已连通，离线1521/1521、25套通过。** [证据](../docs/validation/foundation-listener-clr-20261024/README.md)：502源/22包冻结未安装，Runtime160/Hooks177/Audio573。真实自有native→CLR bridge→生产共享broker core，normal/SEH/C++三场景进入实际ledger；图和根仍controlled。
- **本批实证**：12独立进程1630检查，覆盖三site、双sink、异常、target-read AV无Before的Abnormal(token0)、旧call跨Attach、detach/GC、worker、same/cross-slot nested、foreign-original和观察故障。rawAV不经过broker；非固定游戏receipt回调，非IL2CPP/GC或全部delegate leaves证明。full session21231 exit0，保留111条去重原始编译诊断（含ANSI变体及既有warning）。
- **余项**：真实固定游戏receipt callbacks/冷启动、动态leaves与mutation/provider转交和取消、全部future及消费者资源退休、完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。游戏仍1021安装18项/其326源归档与原游戏三文件不变。


### 历史1515（监听发布与只读采用）


- **历史：生产监听发布与只读采用已通过离线验证。** [证据](../docs/validation/foundation-listener-publication-20261024/README.md)：25 套 1515/1515，493 源/22 包冻结，未安装。固定映像九处发布、V2 416 字节记录与 C# RequireReady 已接通，helper 共42 exports。
- **本批实证**：映像5进程1943检查，含 source/post/relay/chain 四种篡改拒绝；atomic Unknown713检查、V1/V2各100次确定竞态读取；CLR只读采用72检查/2进程。全部不执行游戏代码，listener未附加且调用0；上批自有native listener8349检查继续回归。full session 53963 exit0，保留111条去重编译诊断。
- **余项**：真实游戏冷启动、listener→CLR 回调与 ledger、IL2CPP/GC、动态 delegate leaves、连续 mutation/provider 转交与取消、全消费者退休和完整 Retry/Return/Loader 仍未验收。RequestPlay 关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。完整 B01–B19 继续，仅 B03 整项与 B18a 子项已勾；游戏实测按用户安排暂缓。 当前游戏仍1021安装18项/其326源归档，原游戏三文件未改。


### 历史1515（监听原生层）

- **历史listener原生层批：1515/1515、25套、491源/22包冻结未安装。** [本轮证据](validation/foundation-listener-native-20261024/README.md)：自有native pre-tail/post-continuation 8349项，helper共41exports（新增5）；原硬件返回与动态chain/replay按实际fixture验证，非游戏六区发布。
- **边界**：新listener native only，非CLRlistener/ledger；独立lane非聚合V2，TargetReadFaulted After可无Before，V1Ready144不授权六区；invokable不等于每个delegate leaf。未验分支保留。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；session 45158 exit0，完整构建保留92条去重编译诊断，不宣称零warning或干净环境B19全矩阵复现。
- **余项**：本批listener为native only，不是CLR listener/managed ledger接入；没有固定游戏六区发布，不升级FirstInstall V1 Ready144。invokable dispatch不等于每个delegate leaf；真实游戏receipt callbacks、冷启动、IL2CPP/GC、连续mutation/provider转交/取消、全部future来源与消费者退休、完整Retry/Return/Loader仍缺。 RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。 游戏仍1021安装18项/其326源归档。


### 历史1515（采用回调联合批）

- **历史采用回调联合批：1515/1515、25套、484源/22包冻结未安装。** [本轮证据](validation/foundation-adopted-broker-20261024/README.md)：同生产采用/dispatch core在自有native fixture真实回调379项/3进程，factory1、broker安装0；不是固定游戏receipt callbacks，metadata受控。
- **实证与边界**：happy123/fault181/late75；双sink/token、旧call跨Attach无新broker交付、原SEH/C++后恢复、stickyUnknown和重绑后GC仍可回调已验。未单计raw CLR入口；Info56不读回mid，生产postVerify失败仅源码复核；Runtime160/Hooks171/Visual71/UIinput79/AudioContent573。最终session 11559 exit0；完整构建保留109条去重编译诊断，原文见build-summary；不宣称零warning或干净环境B19复现。
- **余项**：真实固定游戏receipt的native→CLR回调、Doorstop冷启动顺序、实际IL2CPP metadata/listener/GC、未来来源与连续mutation/provider转交/取消、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 实测稍后，游戏仍1021安装18项/其326源归档；完整B01–B19 active，仅B03整项/B18a子项勾选。

### 历史1515（CLR启动采用批）

- **历史CLR启动采用批：1515/1515、25套，482源/22包冻结，未安装。** [本轮证据](validation/foundation-clr-preinit-20261024/README.md)：实际producer→生产CLR RequireReady/RuntimeAdopt与双idle sinks接入，零游戏/native callbacks，metadata受控。
- **实证**：CLR59检查/2进程；atomic Unknown328检查、暂停writer期间100读；同生产C init 259检查/16进程。首SEH测试Flags0假设已按first-chance/捕获128一致修正，仅改测试；最终session 57905 exit0。本次完整构建保留92条去重编译诊断，原文见汇总；不宣称零warning或干净环境B19复现。
- **余项**：真实Doorstop冷启动顺序、生产native→CLR观察回调、实际IL2CPP元数据/listener/GC尚未验收；连续mutation/provider转交与取消、未来来源、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 实测稍后，游戏仍1021，完整B01–B19 active，仅B03整项/B18a子项勾选。

### 历史1515（原生启动安装批）


- **历史1515/1515、25 套（Runtime160、Hooks171、Audio573）完整构建通过，474源/22包冻结，未安装。** [原生启动安装](validation/foundation-preinit-producer-20261024/README.md)：实际 Doorstop pre-init 路径已接原生 producer；先校验固定游戏与 Dobby 身份，准备 runtime-invoke、UnityEvent 父入口和内段 relay，再按固定顺序发布三处写入。CLR 采用已存在的身份和 epoch，不重新安装这两个通道。
- **实证**：native runtime274；写前5进程100；实际映像175（不执行游戏代码），Ready重入转Unknown且无第4写。生产CLR同进程联合采用和真实冷启动仍待验证。完整构建保留 108 条去重编译诊断（包括 upstream native warning），详见 build-summary；不宣称零警告或干净环境复现。
- **余项**：真实 Doorstop 冷启动时序、生产 CLR 回调与 IL2CPP/listener/GC 尚未验收；全部 listener/future 来源、连续 mutation/provider 转交和取消、全消费者资源退休及完整 Retry/Return/Loader 仍缺。RequestPlay 保持关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 下一步先做真实CLR采用，再闭合冷启动与全部消费者。完整B01–B19仍active，仅B03整项/B18a子项勾选，实测稍后；旧1021安装18项及游戏3hash不变。

### 历史1511

- **历史1511/1511、25套；Runtime160/Hooks167/Audio573完整构建冻结暂存，未安装；游戏仍1021。** [启动观察接入](validation/foundation-first-install-20261024/README.md)：新增native bootstrap观察通道与adopt-existing托管桥：每次Enter固定一次immutable observer快照，旧调用即使跨过Attach，也不继承新Mid/Exit；新调用三回调按实际native token/原caller/returnPC配对。CLR只采用已经配置的slot/Target/Original/Entry/epoch，不再次Dobby或Configure原通道。
- **证据与范围**：453源/18暂存，较1507新增8/修改4/删除0。源码ZIP `fcdba98f9c3b160ac0ac64e38575a5bcab6406d1935964a2fa82b2efacb5381c`；暂存ZIP `a3e5ec54a8f4c6be5a51766162ec309cebb08ca47c4bd90d36ea08de340ec597`；full exit0，日志SHA `6e01a7e68ea8eac671909b73105c4166dabe2f1dc60d118db89cc415f2ee5ac8`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志0条不同编译诊断；首次full exit0单独保留，静态审查修正旧Info快照竞态后再次完整构建通过。 native 698断言/2旧+18新调用、实际CLR六次原调用和3组ABI/拒绝回归分别通过。实际Doorstop pre-init安装器/生产producer与首次安全发布尚未实现或验收，游戏mid未发布。bootstrap与旧allocator共用native表的统一槽责任、broker受限采用、真实IL2CPP/listener/GC、连续mutation/provider转交/取消、全消费者资源退休及完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。
- **后续**：实际Doorstop pre-init producer、统一槽责任、broker/ledger受限采用，再做真实listener与全run退休。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。

### 历史1507

- **历史1507/1507、25套；Runtime159/Hooks164/Audio573完整构建冻结暂存，未安装；游戏仍1021。** [原生调用准入](validation/foundation-void-admission-20261024/README.md)：新增原生调用准入门：Close停止新调用进入CLR，等待已准入的Enter/original/Exit全部完成后，签发绑定槽、全局epoch和OS线程的凭据。同步同槽递归继续；任何void槽内自Close被拒绝；超时自动重开，错误Release不开门。运行时可选私有lease在写尝试/Unknown后Dispose保关闭；Unknown贯通bridge。
- **证据与边界**：445源/18暂存，较1496新增5/修改7/删除0。源码ZIP `95d43b33aad52ef66ce7079afe5f2ce6b2d9cf638bc5269d6b6b97d34b38e651`；暂存ZIP `438f50f5e98d9ea1473abdc28c09d9adeb30a6260cde89f1f19d17879ea1edb8`；full exit0，日志SHA `b14808538861beecf1e0685e43b17ddd8ecdc904543564c5cb754780b5d1bf13`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志0条不同编译诊断；首轮完整通过到Package后runner转义错误退出1，原日志保留，补强CLR probe后重跑最终full exit0。 独立native 656断言/15原调用、实际CLR probe和11组受控协议分别通过。首个父detour安装和生产mid安全发布尚未证明，当前patch未发布；真实IL2CPP/listener、连续mutation/provider转交/取消、全部消费者与资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。
- **后续**：首次安装屏障和固定publisher、实际listener/future来源封口及完整资源退休/Retry/Return/Loader。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。

### 历史1496

- **历史1496/1496、25套；Runtime159/Hooks153/Audio573完整构建冻结暂存，未安装；游戏仍1021。** [事件父链联合验证](validation/foundation-event-mid-parent-20261024/README.md)：新增6条隔离的联合原生回归：真实Dobby父入口→原生函数五字节CALL→mid helper→CLR bridge→broker当前私有凭据→ledger同代次列表采用。固定entry+0x140返回地址由原生标签取得，Dobby后中段CALL保持；空列表/外来递归、worker尾窗口/解绑、采用故障、成功采用后原生SEH和C++异常、跨代旧凭据拒绝均通过。
- **证据与范围**：440源/18暂存，较1490新增1/修改5/删除0。源码ZIP `9a251c8c6a8d689c7f2af92cd1c58e6b48a31cb2f69fc9ffdeea8463fb2e98c2`；暂存ZIP `004b0f9ae2c3d6d9fca2860354fd7db0c169f672a8929d609ec2bb7c2da7fb00`；full exit0，日志SHA `d84319c2f14832c43aec98118eb0135f8d9fe2f22305c4c7616aebe64c520207`。旧1021安装18项、旧326归档及原游戏三hash匹配。完整日志含0条不同编译诊断，实际信息见汇总，不宣称干净构建无warning。 新增6个Audio原生联合用例，独立mid3559仍通过。列表图和持根仍是受控backend，未证明IL2CPP GC或真实游戏对象。生产mid并发指令发布仍未解决，patch未发布；实际listener执行、连续mutation与动态转交/取消、全部消费者/资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。
- **后续**：生产首次执行屏障和mid发布，实际listener/future来源封口，以及完整资源退休/Retry/Return/Loader。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。

### 历史1490

- **最新1490/1490、25套；Runtime159、Hooks153、Audio567完整构建冻结暂存，未安装；游戏仍1021。** [事件内段上下文](validation/foundation-event-mid-20261024/README.md)：新增15组受控回归（Hooks9、Audio6）及独立原生mid fixture的3559个断言。原生桥保存GPR、XMM低128位、RFLAGS、MXCSR和x87控制字；物理凭据限定同一父调用、线程、sink和代次，旧凭据不能跨复用context重放。Adapter只在凭据有效时独立持根并采用执行列表。
- **证据与冻结**：439源/18暂存，较1475新增8/修改8/删除0；旧1021安装18项、旧326归档和原游戏三文件hash匹配。源码ZIP `a773d9f963b2d3223715765460b91d18be748a561e0178ba5e1363f0205decb1`；暂存ZIP `d850301a4d0355f2f5ce68b5332ccb34cb26fa2e5f7d96463dd2956b0266d9a9`；完整构建exit0，日志SHA `10c5ba2c4f7bada508a46fa3785a77ab64f66116181963962c9a24d632593a82`。完整日志17条不同编译诊断均为测试warning，原始日志保留；首轮MASM XMM尺寸错误已修，定向native及full通过。 mid3559真实原生断言另列，nativeParentVerified=false/gameRuntimeVerified=false；23范围/1706指令/6E8旧证据再核，新site148指令单列。
- **剩余**：生产mid patch未发布：五字节并发指令发布未证明，真实native父调用→mid→managed Adapter联合链路尚未验收。POD列表与受控root不能替代IL2CPP GC或游戏实例；三条实际listener调用、连续mutation、动态转交/取消、全消费者/资源退休及完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。 用户“实测稍后”保持；B01–B19 active，仅B03整项/B18a子项勾选。无本批安装/UI/游戏进程终止/原文件修改/commit/push。

### 历史1475

- **最新1475/1475、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [原生void父分派](validation/foundation-native-void-20261024/README.md)：Runtime159/Hooks144/Audio561，较1449新增26组（真实native4、受限resolver2、Hooks17、事件层3）。UnityEvent.Invoke已从旧managed-original void观察切换到独立NativeVoid0通道：原函数在native层一次执行，实际caller/MethodInfo进入同代次并核exit，全线程probe保留原snapshot配对。原生32永久槽、broker4096同时记录有界；worker不持安装Gate，unknown保责任，逻辑解绑不伪造native drain。Invoke/独立Prepare及每次图读前后均检查共享native Known。
- **真实证据与冻结**：新增4组Windows x64真实Dobby/MASM caller、worker/detach、SEH/C++ code/kind/payload与native callback21组合通过；不与受控17组和Adapter图测试混淆，也不当游戏实机。431源/18暂存，较1449新增7/修改14/删除0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态沿用23范围/1706指令/6E8候选并重新核字节，不新增game调用路径结论。源码ZIP `5cf3a1367e75af063ac372e0463025142c35f18b2a4c268e3b82d3b38be40490`；暂存ZIP `dda28b995df7c67b8cf06656b712ee551942ec0ed9ef4fab568c5cc542678a65`；full exit0，log `4326638772c5dcdbfd1ee17a23b2238b3e2aaf1f6609033292eb93284d561c8d`。首full Runtime159/Hooks144已过但Audio fixture漏INativeDetourEngine编译失败，修正后定向Audio561与full全过；失败日志保留。完整增量编译诊断0条，前置日志既有warning保留，不宣称clean build零warning。三代理实现/有限复核，root唯一builder，cache `D:/RizToolbox-build-cache/native-void-final-20261024`，所有构建结束。
- **直接下一步**：[inline执行与取消](validation/foundation-native-void-20261024/next-integration.md)。内联执行集合/三条实际listener调用点、连续mutation、listener/provider转交、销毁取消和future来源封口仍缺；Pending保留、InlineExecutionUnobserved=true、DispatchPathVerified=false。全部消费者和资源退休、完整Retry/Return/Loader继续缺，RequestPlay关闭。 真实native fixture不证明UnityEvent游戏实例、生产metadata/GC故障或全run结束。完整B01–B19 active，仅B03整项/B18a子项勾；用户“实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。

### 历史1449

- **1449/1449、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [UnityEvent来源与监听图](validation/foundation-unity-events-20261023/README.md)：Runtime153/Hooks127/Audio558，较1412新增37组（metadata14、listener13、跨owner故障10）。真实UnityEvent来源、listener图与Invoke前后观察已接coordinator；独立Prepare单独记账，固定版本Invoke内联Prepare，明确InlineExecutionUnobserved=true/DispatchPathVerified=false。Source/initial/terminal/active读取与清理补共享launch fault屏障，局部free失败和外部authority故障分开处理。
- **验证与冻结**：424源/18暂存，较1412新增5/修改15/删除0；旧1021安装18项、旧326源码归档及三原游戏hash匹配。静态23范围/1706指令/6直接E8候选及三输入hash核对，另三函数264指令明确Prepare内联。完整增量0编译诊断；定向构建17条既有测试warning保留。首轮153/153与548/548通过；补跨owner屏障后556/558的两个fixture身份复用错误已修，最终558/558；初次full末尾SDK restore遇NuGet EOF，重试依赖成功后完整再跑通过，失败日志保留。源码ZIP `4c3dd3eff3e9701c9ac161b5986012de332f009c538c1c6a2eafc4dba7e17332`；暂存ZIP `8d416e727f052ddbdf845996f56d51fda7ba2aecd77362f4f9e4018d534f4239`；build exit0，log `b1680db7312d37389c366ff5eb74b23ce19a0b40845e2561c8348656dff6db95`。三代理并行实现与有限交叉检查，root唯一runner；native cache `D:/RizToolbox-build-cache/unity-events-final-20261023`，所有构建结束。
- **直接下一步**：[inline执行与完整消费者协议](validation/foundation-unity-events-20261023/next-integration.md)。监听列表连续mutation、inline实际执行路径、listener/provider转交、销毁取消、全部active消费者与资源退休、完整Retry/Return/Loader仍缺；Pending保留，不签whole-run结束或Close，RequestPlay关闭。 受控broker/metadata、部分实际backend的FakeNative故障回归不代替生产metadata初始化、真实listener执行、native original异常展开或新backend游戏验收。B01–B19仍active，仅B03整项/B18a子项勾选；用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原游戏文件修改、commit/push。

### 历史1412

- **1412/1412、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [Cutout直接异步协程](validation/foundation-cutout-async-20261023/README.md)：Runtime139/Hooks127/Audio535，新增19组。reset SimpleDelay和每Trigger延迟事件从真实typed IEnumerator Start/Auto接入，要求actual EventTrigger host、物理StartEvents代次和caller38A60C/38A6B2；original前独立root并发布enum，承接同步firstMove和<=0同步false。Delay支持provider state1和Wait state2，event固定0→1→false，实际Action/Trigger/events/offset保持本record身份；64记录/128根可撤身份redrain后独立free/代次复用。
- **接线与冻结**：Source纯物理窗口、activation前Capture、coordinator Ready帧泵回收及active destruction前后原生idle屏障均已接；读内launch/source故障也拒绝后续native读/发布/free。419源/18暂存冻结，较1393新增3/改5/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态21范围/1592指令和6直接E8候选再核对；完整增量0编译诊断，前置17既有测试warning保留。源码ZIP `8631af3deacab76ede9b9bb85ed0c28f8478a4b66644cc5f5e5240671348a4c3`，暂存ZIP `0e86c65799838c9912f1421b896b2d5633890198f15db1deb33c07203e460158`；build5141 exit0，log `8d02c38c96d3af1fb4536843c05f562f87f10e2d146f0b1a342cceaec4a99955`。native cache `D:/RizToolbox-build-cache/cutout-async-final-20261023`，构建全部结束；首轮534/535 fixture计数器失败日志保留，修后535/535。三代理被用户明确重启后测试/backend/独立只读审查完成，root唯一builder，无第二runner或游戏验收。
- **直接下一步**：[listener、取消与全消费者封口](validation/foundation-cutout-async-20261023/next-integration.md)。Source仍Pending/分派后UnsealedDispatchSource，未自然false的销毁取消路径、动态listener/provider转交、音频/全部future来源关闭仍缺；不能发全run结束token或Close。完整active退休/Retry/Return/Loader继续，RequestPlay关闭。继续实际UnityEvent的persistent/runtime/executing listeners Target/Method/列表变化及动态provider；音频图、globals/menu、result callbacks/future sources全部取得真实结束证据后才VerifyConsumersEnded→Close→bridge/bundle/handler/coordinator退休及完整命令/Loader。B01–B19仍active，仅B03/B18a勾；用户“先继续代码，实测稍后”保持，无本批安装/UI/游戏输入/进程终止/commit/push。

### 历史1393

- **1393/1393、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [活动销毁与事件来源](validation/foundation-active-destruction-20261023/README.md)：Runtime139/Hooks127/Audio516，新增26组。实际factory五对象的独立销毁观察与Cutout EventSource已在激活前捕获，Ready/销毁阶段帧泵推进；required Cutout完整退出后须更晚真实帧核controller/GO/Transform dead，staging只对自己的空/未激活对象Destroy一次再等后帧。实际EventTrigger/GO/Transform、32上限triggers/UnityEvent/offset持根，StartEvents真实probe与计数纳入共享屏障，expected-dead保closure。
- **冻结与限界**：416源/18暂存冻结，较1367新增7/改3/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态16范围/1399指令及6直接E8候选字节核对，完整增量0编译诊断，定向17条warning保留（15既有、2新增测试计数器CS0649）。三个子代理实现/独立有限审查，新增26组受控检查；生产Unity Destroy/free、source get_Item quota故障及新backend游戏尚未直接验证。源码ZIP `aa750bb9cec77862762a3166b787244f3a0ac5a544c836fc0dd2fd3c8d970d92`；暂存ZIP `2deac1ff398f50fb54b9ea4dbb6783ad33794453b5522e5772cb7585539df789`；build90989 exit0，log `9e451d404eed01cf729e7126e33e46240f7e47100c87881f201b49cda735d259`。native cache `D:/RizToolbox-build-cache/active-destruction-final-20261023`，build已结束。
- **直接下一步**：[外置异步与完整消费者](validation/foundation-active-destruction-20261023/next-integration.md)。EventSource仍Pending，分派后UnsealedDispatchSource=true；两类外置延迟及动态listener尚未闭合，五对象dead不代表音频图/globals/menu/全部回调结束。全active verifier、资源/协调器退休与完整Retry/Return/Loader仍缺，RequestPlay关闭。继续实际EventTrigger SimpleDelay/StartEventsEnumerator、UnityEvent的listener与转交工作、音频图、全部future sources封口、globals/menu条件恢复，再真实consumer-end verifier、Close/bridge/bundle/handler/coordinator退休与完整命令/Loader。B01–B19 active，仅B03/B18a勾选，用户“先继续代码，实测稍后”保持；无本批安装/UI/游戏输入/进程终止/commit/push。

### 历史1367

- **1367/1367、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [协程观察根回收](validation/foundation-terminal-recycling-20261023/README.md)：Runtime139/Hooks127/Audio490，新增7组；先撤Published、保enum/代次再读真实native activity，非零待后帧，未知保责任；enum/Coroutine独立根各只free一次，确认后才复用slot。64次/原始地址复用/角色切换及worker/reentry/释放失败已覆盖。Ready帧泵继续观察future factory并回收initial已结束观察者，不签全run结束。
- **冻结与审查**：409源ZIP `aecd80193c7f35162444f7e34951a72f3cf294f198e7c5d412a7f831f414859c`，18暂存ZIP `f98e5f148dc65dd8091577b09a498a2c7ac75e1518d0b65bdf0b533777525fbe`；完整build1689 exit0，log `c7834d5a1968ac976518193780f78b1f196ac470c51eea9b74618d4af222dbb8`。较1360增1/改5/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态12范围/991指令和6 E8候选再核对；完整增量0编译诊断，前置Audio15既有warning保留。activation_finish复核读内fault修正，settings_finish核Reset及外置EventTrigger，never_active_retirement_code给真实active顺序；没有独立runner。新增fault仅受控backend，非生产free注入或游戏验收。
- **直接下一步**：[确切active对象失效与完整退休](validation/foundation-terminal-recycling-20261023/next-integration.md)。Cutout在3C5963调用另一个EventTrigger.StartEvents@38A460，后者启动自己的SimpleDelay/StartEventsEnumerator，UnityEvent listener仍动态未知；必须独立追踪，不能用controller dead或StopAllCoroutines作全结束。继续实际controller/GO/音频图跨帧失效、future sources封口、globals/menu条件恢复、全消费者verifier、bundle/bridge/handler/coordinator退休和完整Retry/Return/Loader。RequestPlay关闭，B01–B19 active仅B03/B18a勾；用户“先继续代码，实测稍后”保持。本批无安装/UI/游戏输入/进程终止/commit/push，native cache `D:/RizToolbox-build-cache/terminal-recycling-final-20261023`，build已结束。

### 历史1360

- **1360/1360、25套完整构建冻结暂存，未安装；游戏仍1021四门包。** [结算协程](validation/foundation-terminal-coroutines-20261023/README.md)：Runtime139/Hooks127/Audio483，新增17组。typed native Factory ref0、public String Start resolver与Cutout/Fade tracker/backend已实现；生产coordinator激活前安装，actual result capture claim后固定包围ControllerOver，必须观察正确named Cutout/factory/Start发布后才成功。真实Dobby Factory fullref/null/caller/同步首Move发布/SEH/C++与worker/解绑、15组Audio及1 Hook回归通过。
- **仍未完整可玩或退休**：静态确认Cutout最后请求Destroy GO；需实际跨帧失效及active图退休，不能当全部来源结束。terminal当前每局累计16条factory记录，活动根尚不回收，ObservedEnded不撤未来producer。完整Retry/Return/Loader与全资源/协调器退休继续缺，RequestPlay关闭；新backend/Unity反射尚无游戏验收。408源/18暂存和旧1021安装18项/旧326归档/原游戏三hash匹配，10范围/957指令/三输入hash/6个E8候选字节再核对；完整增量0诊断，前置15既有warning保留。用户“实测稍后”保持，只勾B03/B18a。[下一步](validation/foundation-terminal-coroutines-20261023/next-integration.md)。

### 历史1343

- **1343/1343、25套完整构建冻结暂存，未安装；游戏仍1021四门包。** [真实初始化协程追踪](validation/foundation-initialization-coroutines-20261023/README.md)：Runtime138/Hooks126/Audio468，较1324新增19组。生产tracker/backend已接coordinator的setter后activation前、commit前两条实际enum自然完成核对，菜单取新return normal信用前也核对同launch tracker。真实controller/caller/native getter original窗口/rooted enum及闭delegate同时验证；Delay state1/2均支持，未知状态有界保责任。
- **验证边界与剩余**：15组tracker+3组启动/菜单组合、1组Hook窗口及增强真实Dobby getter窗口断言通过；13范围/1731指令及三输入hash/常量再核对。405源/18发布归档，旧1021安装18项/旧326归档及原游戏三hash匹配，15条既有测试warning保留。尚无新backend游戏验收，Loader未组合，RequestPlay关闭。仅两条初始回调结束，其他异步来源、active资源/全局/协调器退休、完整Retry/Return仍缺；用户“实测稍后”保持，只保留B03/B18a。[下一步](validation/foundation-initialization-coroutines-20261023/next-integration.md)。

### 历史1324

- **1324/1324、25套完整构建冻结暂存，未安装；游戏仍1021四门包。** [协程原生边界](validation/foundation-coroutine-boundaries-20261023/README.md)：Runtime138/Hooks125/Audio450，新增9组。内部Start/MoveNext原生transport、bridge/broker和public Start/Auto resolver已实现，真实Dobby验证精确汇编caller（含tail relay）、同步首Move、raw返回、worker/解绑、SEH/C++原异常与清理。8组受控Hook回归也通过；15条既有测试warning保留。
- **本批尚无Adapter tracker调用点**：静态9范围/1574指令确认getter3.1秒Delay与WaitUntil解锁分离，实际返回caller3BEFDA/3BF0B2，两helper尾跳公开153D380而绕过私有Managed2。后续须实际root enum并观察正常false/native drain，不能按state=-1/计时或当前TLS判结束。402源/18暂存冻结、旧1021安装18项/旧326归档及原游戏三hash匹配。初始化callback/锁信用、活动消费者与资源退休、完整Retry/Return/Loader仍缺；RequestPlay关闭，只保留B03/B18a，用户“实测稍后”保持。[下一接线要求](validation/foundation-coroutine-boundaries-20261023/next-integration.md)。

### 历史1315

- **历史1315/1315、25套完整构建已冻结暂存，未安装；游戏仍1021四门包。** [菜单输入与相机交接](validation/foundation-menu-handoff-20261023/README.md)：Runtime137/Hooks117/Audio450，新增17组。显式normal/back信用、相机/粒子写入的连续归属账本及native backend接入实际启动类内；首次转场后释放自己的normal信用，commit前再次核对。全线程标量probe和独立early-retained lane保留旧回调顺序；ABA、后继对象、worker/重入和未知责任均有受控检查。
- **仍未完整可玩**：返回菜单方法有实际同局owner校验，但完整Return/Retry/Loader整链未接。getter隐式信用/延迟回调、完整active消费者与资源/coordinator退休仍缺；新菜单四入口尚无实际Dobby/game或original unwind验收，相同GO别名未适配。395源/18发布冻结匹配，旧1021安装18项/旧326归档及三原游戏hash不变；10范围/282指令与三输入hash再核对。15条既有测试warning保留，无新安装/游戏操作/B勾选，RequestPlay关闭，用户“实测稍后”保持。

### 历史1298

- **1298/1298、25套完整构建已冻结暂存，未安装；游戏仍1021四门包。** [native Judge在途与退休前drain](validation/foundation-judge-drain-20261023/README.md)：Runtime137/Hooks113/Audio437，新增11组。实际helper GetStats及内层Judge/Hold活动已纳入LOCAL准入、终止快照、撤身份前后drain与停止；统计/上下文故障保持unknown，exact lookup不建第二patch。真实Dobby/C++fixture验证worker阻塞、逻辑detach及SEH/C++ original清理；不覆盖managed inner/Hold原异常展开或游戏全矩阵。
- **完整活动退休仍缺**：inFlight=0只为当前观察；菜单锁/相机、未来旧回调来源、controller/音频失效、globals/bundle/coordinator释放及完整Retry/Return/Loader组合仍需补齐。390源/18发布已冻结，旧安装18项/旧326归档及三原游戏hash匹配；无新安装/游戏操作/B勾选。用户“实测稍后”保持，RequestPlay继续关闭。

### 历史1287

- **1287/1287、25套完整构建已冻结暂存，未安装；游戏仍1021四门包。** [返回场景与Boolean观察](validation/foundation-return-primitives-20261023/README.md)：Runtime136/Hooks107/Audio433，新增21组。返回源绑定同局实际已完成首次转场和Returning registry，验证真实operation、目标loaded、原Level卸载；精确一字节Boolean观察为菜单SetActive账本提供基础，但尚未绑定菜单。387源/18发布分别归档，旧安装18项/326源归档及三原游戏hash匹配。
- **整体使用边界继续**：没有完整Return/Retry命令或Loader组合，菜单相机/粒子/输入锁、全native Judge在途与active消费者退休仍缺；scene receipt不证明全部消费者结束，RequestPlay保持关闭。新Boolean仅受控engine/calli验证，未有真实Dobby/game或native original unwind证明。完整增量0编译诊断不等于干净重建零warning；前置Audio15条既有warning保留。无新安装/游戏输入或B勾选，用户“实测稍后”保持。[架构参考](2026-10-22_架构-InFalsusTools-reference-report.md)已复核。

### 历史1266

- **1266/1266、25套完整构建已冻结暂存，未安装；游戏仍1021四门包。** [启动激活与首次转场](validation/foundation-actual-launch-20261023/README.md)：Runtime136/Hooks99/Audio420，较1216新增50组离线检查。生产 coordinator 已实际调用 bundle/四池预留/inactive clone/setter/受控activation/真实scene transition，并接入host帧泵，等待新loaded Level和实际Start/getter图后才commit。设置使用真实setter基线及游戏MappedSpeed、offset/1000f校验；结果sink先发布owner再建UI，回调真正退出才承认其UI消费者结束。
- **新实现仍有明确使用边界**：Loader/模块未构造bridge/handler/sink/coordinator完整链，RequestPlay仍关闭；菜单相机/粒子/输入锁、完整Retry/Return、新局事务及活动局资源/协调器退休尚缺。383源/18发布文件各自冻结并核对，旧1021实际安装18项/旧326归档及原游戏三hash匹配。转场7范围/1895指令已再次磁盘核对；完整增量0编译诊断不等于干净构建零warning，前置Audio15条warning保留。没有为1266另跑独立ABI runner；无游戏实测或新增B勾选。用户“先继续代码，实测稍后”继续有效。

### 历史1216及更早

- **历史1216/1216、25套完整构建已冻结暂存，未安装；其时及当前游戏仍1021四门包。** [原生初始化异常与发布预留](validation/foundation-initialization-native-unwind-20261023/README.md)：Runtime131/Hooks99/Audio375。生产Start/getter现由typed native helper执行original，Prepare→Configure→Commit先配置后发布；native finally保证完整TLS/观察者清理和在途。1747项新native helper检查及生产真实Dobby ordinary/LOCAL SEH/C++八种路径通过；独立runner使用同包Runtime/SDK/helper再次通过。371份声明源码与18发布文件分别冻结，旧安装18项/旧326源归档及三原游戏hash重新匹配。完整增量0编译诊断，前置Audio的13条FakeNative CS0649保留。
- **1216补齐的发布预留与当时边界**：同32条graph record在native尝试前预创建两枚身份与固定发布槽，只一次填原生地址后Volatile发布；撤回/退休不再创建新发布数组。3组回归覆盖半成品不分类、inflight字典hash稳定及progress槽复用，controller map撤回/redrain/复用组合尚缺直接新fixture；不宣称全系统native后零分配或完整I04验收。Play/Progress/Timeline的original仍沿旧managed输送，新Start/getter异常证明不覆盖它们。实际Loader launch入口、activation/Awake/设置/globals、commands/result view、Retry/Back和active退休仍未接通，RequestPlay继续关闭；未安装/实测，不增加B勾选。

- **历史1210/1210、25套完整构建已冻结暂存，未安装；其时及当前游戏仍1021四门包。** [精确初始化隔离](validation/foundation-initialization-isolation-20261023/README.md)：Runtime131/Hooks96/Audio372，新增23组离线检查。可调用Start/getter/Play/Tooltip sealed接线共享原Mode entry，原调用前核验实际Start/info/progress与owned params闭包；ordinary子scope遮蔽旧Timeline权限、全部相关入口真在途入drain，worker fault立即撤ready。364源ZIP/18staged ZIP逐项匹配，旧1021实际18安装/旧326源归档及三原游戏hash独立匹配。完整日志13条共享FakeNative CS0649，无编译错误；早期CS1501/旧retry断言失败保留。helper31261/61358、新native-only18含EDXpoison；真实Dobby初始化case在完整Runtime及[同包独立runner](validation/foundation-initialization-isolation-20261023/independent-native-abi/README.md)均通过，仍gameRuntimeVerified=false，native original SEH/C++跨CLR展开未证。

- **1210当时限界（发布预留由1216继续补）**：src无Loader/模块生产调用实际bridge Create/handler构造/LaunchReservation.Reserve，用户LOCAL初始化路径未启用，RequestPlay仍OperationUnsupported。I04的四池/五根槽已预留，身份及immutable发布容量尚非全部native前预分配；这是明确下一实现。实际activation、commands/result view、六globals和active退休仍缺，间接/异步SaveSteam全覆盖未证。本轮未安装或实测，不新增B勾选；见[I01–I08逐项边界](validation/foundation-initialization-isolation-20261023/README.md)与[独立审查](validation/foundation-initialization-isolation-20261023/independent-review.md)。

- **历史1187/1187、25套曾构建并冻结暂存未安装。** [激活前责任预留](validation/foundation-launch-reservations-20261023/README.md)：Runtime130/Hooks82/Audio364，较1148新增39组Audio检查；helper fixture31261/61358。native尝试前预留capture/factory/queue/graph原有配额；controller交接保持Held，实际graph采用/核验和drain后沿同一registry commit发布shared ready；worker早到terminal阻止提前ready/capture。352份声明输入ZIP与18份staged-package.zip仍可独立复核，旧1021实际18安装/326源归档及三原游戏hash当时匹配。完整增量编译诊断空，前置整合失败日志原样保留。[交付核对](validation/foundation-launch-reservations-20261023/independent-review.md)范围有限，不当成游戏验收。该时初始化hooks尚缺，1210已补可调用实现；actual activation/active退休与完整sink/commands仍缺，本轮暂缓实测，不增加B勾选。

- **1187后的静态路线已部分落实**：[初始化顺序](validation/local-initialization-order-20261023/README.md)固定9范围/27锚点/3场景组件；Start末尾直接Mode/Tooltip、Play独立Mode尾调均在terminal Update之外。[实施任务](validation/local-initialization-order-20261023/next-initialization-isolation.md)要求的共享Mode、Start/getter原调用核验、Tooltip/Play around及真在途已进入1210可调用代码；完整I04、实际启动coordinator和游戏验收仍缺，不能宣称Save/Steam完整隔离。

- **历史1148/1148、25套曾构建暂存未安装。** [终止隔离与有限退休](validation/foundation-local-termination-20261023/README.md)：Runtime130/Hooks82/Audio325，新增67组；两个helper fixture31261/61358。补第五LevelOver门、Progress/Timeline精确上下文、真实在途/帧桥接、晚帧单次捕获队列与never-active封闭退休；旧helper记录覆盖限制与active verifier缺失明确。依赖就绪先于admission，factory固定session和32槽保责任；该时原生激活前预留待下一步，1187已补其中管理基础设施。18当时暂存/346源码归档与旧1021安装18项/326归档当时分别核对，原游戏三文件不变。13条共享FakeNative CS0649保留，无编译错误；无新游戏实测/安装/RequestPlay开放，不增加勾选。

- **历史1081/1081、25套已构建暂存未安装。** [参数/结果步骤](validation/foundation-local-parameter-binding-20261022/README.md)：Runtime130/Hooks69/Audio271；新增43组，真实pending参数setter后端、唯一claim实际ControllerOver捕获、bounded void/field/value native调用已实现。18新暂存/337新输入、18旧安装/326归档及三原游戏文件分别核对通过。独立审查发现周边读free责任和borrowed root依赖已修；setter所有异常有界保守保留的限制明确。没有新游戏实测、激活handler或RequestPlay开放。[终止归属](validation/local-result-ownership-20261022/README.md)与[消费者结束](validation/local-consumer-retirement-20261022/README.md)只给静态路线与缺口，无active原生结束token。不增加勾选。

- **历史1038/1038、25套曾构建暂存未安装，安装当时与当前仍1021。** [对象/参数交接修复](validation/foundation-inactive-publication-20261022/README.md)：Runtime115/Hooks69/Audio243，新增17组实际runtime seam+受控native回归；parent/clone输出先登记，字符串第8根/bool缓冲显式责任，bundle128槽先持publication输入，配额/清理失败不丢责任或重试。18暂存文件/329输入与旧1021安装18文件/326输入归档分别核对通过，原游戏三文件不变。新源码未在游戏实测；旧953的7根证据不覆盖新8根。
- **1021保护宿主已安装，实机按用户要求延后。** [构建及宿主实现](validation/foundation-local-protection-host-20261022/README.md)/[当前会话前缀](validation/runtime-local-protection-host-20261022/README.md)：四门实际metadata/RVA解析与绑定通过，普通ControllerRetry原调用一次；ResultStart/ResultRetry/ResultBack正向未完成。1280连续事件0Error只指前缀，非完整停止会话。AcceptingNewRuns=false；没有LOCAL事务handler、首次结果receiver归属或激活验收。完整B01–B19继续，无新勾选。

- **历史1007/1007、25套曾构建暂存未安装，该时游戏为999；当前安装已更新1021**。[新前置门批次](validation/foundation-local-command-gates-20261022/README.md)新增精确暂停Retry/结果Retry/结果Back，共用Start前置分派，Hooks69含每命令cdecl/异常/worker/重入/独占回归；Runtime115、Audio212。18暂存hash/322当前输入与当前999安装18文件分别匹配，原游戏三文件不变。15条FakeNative CS0649警告保留。没有生产绑定或新门实机，RequestPlay继续关闭；[下一事务计划](validation/local-launch-20261022/next-activation-transaction.md)不能代替结果归属/激活验收。

- **历史999/999、25套当时已安装并完成独立资源包实测**：Runtime115/Hooks61/AudioContent212；18安装hash/320输入及原游戏三文件匹配。320补记templates/tools/packaging/global.json，不能与旧303简单差分。完整增量日志0诊断，前置定向15类FakeNative CS0649和3条CS8602仍保留。[构建](validation/foundation-local-bundle-20261022/README.md)/[879事件实测](validation/runtime-local-bundle-20261022/README.md)：845Info/27Debug/7Warning/0Error。两图共18独立已记录对象身份；来源/caller撤回和原生GC后可读，先退休first不影响second，最终六Unity-dead、10direct+6source根释放、CPU prepared2→1→0、song/content0。只确认游戏停止，未观察正常退出操作；marker和原创fixture已恢复。
- 本批补齐完整owned native bundle与显式publication故障责任（Runtime17新检查、Audio29新检查），仍未激活LOCAL controller。judge复制真实普通局四值但单位未验；参数speed/offset仍构造前初始化0，score为sentinel。首次结果receiver归属、Start/Retry/Back保护生产接线、设置初始化、激活/Awake正向、六globals恢复及消费者结束仍缺。RequestPlay继续OperationUnsupported，B15未验收。以下953等均为历史包边界。

- **历史953/953、25套已构建安装并完成inactive controller实测**：Runtime98、Hooks61、AudioContent183；收尾18安装hash/303源码匹配、原游戏三文件不变。完整增量构建0诊断，前置定向15条FakeNative CS0649 warning保留。[构建](validation/foundation-inactive-controller-20261022/README.md)/[775事件实测](validation/runtime-inactive-controller-20261022/README.md)：746Info/25Debug/4Warning/0Error。普通卡片来源发现→inactive parent下精确clone→Awake0/globals未变→后帧原生GC读回→Destroy→后帧确认controller/GO/parent/两Transform dead→7root及观测根释放。游戏已正常退出、marker恢复Absent。数组协变误拒的两次失败原样保留，不算成功。
- **上一907/907、25套参数实测独立保留**：Runtime92、Hooks61、AudioContent143；当时收尾18安装hash/294源码输入匹配，原游戏三文件不变。最终增量日志0诊断，前置定向14类FakeNative CS0649 warning仍保留，不推断干净重建零警告。[构建](validation/foundation-local-parameters-20261022/README.md)和[205事件实测](validation/runtime-local-parameters-20261022/README.md)：两个独立借用参数owner经原生GC五字段读回、独立释放、wrapper撤回拒读、false Release保留资源及Sprite/Audio/Texture后帧销毁。没有controller或保存调用。
- **globals恢复算法仍以离线为主**：64槽root责任、单播Target、六字段条件恢复/后继保留及controllerSet复核，现有14组CPU回归。953只实测同一owner的六null回调/false flag基线与不变，没有非空delegate Target、发布/恢复或消费者结束证明。未激活clone已有有限实机，实际激活一次时序仍缺。参数Speed/Offset构造0仅是原版设置初始化前值；[后续消费者](validation/local-launch-20261022/parameter-scalar-consumers.md)会覆盖两字段，须验证完整初始化，不盲设1或猜单位；空Chart/JudgeData不可玩。
- **上一895/895基础包与启动smoke独立保留**：Runtime92/Hooks61/AudioContent131，当时18安装hash/290源码匹配；固定Start前置门、宿主会话/一次结果claim、精确revision续持和借用参数factory已集成。[构建](validation/foundation-local-session-20261022/README.md)及[193事件smoke](validation/runtime-local-session-smoke-20261022/README.md)当时未构造参数或安装LOCAL policy，0Error/2等待Warning。相关基础继承到907，游戏实际结果门接线仍缺。
- **上一846/846包metadata独立实测保留**：Runtime92、AudioContent100；当时18安装hash、283源码输入与三个原游戏文件匹配，14种测试FakeNative CS0649警告。252条连续事件、0Error/3等待或挂起Warning；三个metadata图经原生GC读回九getter/颜色、独立撤回与30条owner root记录释放。marker恢复Absent。见[构建](validation/foundation-local-metadata-20261022/README.md)及[实测](validation/runtime-local-metadata-20261022/README.md)。不证明native heap回收或LOCAL可玩。
- **下一步接完整启动/结果事务**：未激活clone创建/清理已补齐，仍需完整owned资源bundle、提交前session身份/结果保护、参数注入/原版设置初始化、激活一次与globals发布/条件恢复、消费者结束证明、一次真实结果/自有面板/返回与新图重试。普通metadata From=LevelList。[静态启动报告](validation/local-launch-20261022/README.md)是实现前基线，不能用历史live-source hash要求新broker相等。本包RequestPlay仍OperationUnsupported，B15未验收。
- **上一 814/814 包的 AudioClip 包装器实测单独保留**：Runtime69、AudioContent91；当时 0 编译诊断、18 安装 hash 匹配、276 源码输入未变。218 条连续事件、0 Error，验证专用 ResourceReference<AudioClip> 构造/getter/Release，false 保留/true 销毁及跨帧显式清理。见[构建](validation/foundation-resource-factory-20261021/build-summary.json)及[实测](validation/runtime-resource-factory-20261022/README.md)。以下较旧包记录按各自日期和包理解。

- **历史793/793完整包已安装并实测**，0编译警告/错误、18安装hash/270源码匹配、原游戏三hash相同。原版ControllerOver借用已有root，实际取得AssetRefReference<AudioClip/Sprite>的官方Release派发，RVA B115D0；1335条连续日志、0Error，原版Failed结果与开局正确关联。游戏退出、metadata marker恢复。当时专用ResourceReference<AudioClip>与本地native wrapper尚缺，已由814及846后续分别补充有限证据；完整LOCAL播放仍缺，不能因采样成功勾B15；见[证据](validation/runtime-resource-owner-20261021/README.md)。
- **历史789/789、25套回归已安装并完成局部实测**，0编译警告/错误、18安装hash和268源码输入匹配、原游戏三个文件hash一致。修复FakeMainMenu返回页与探针迟到通知句柄丢失；930条和244条完整会话证明返回窗口重建、选择generation2释放→generation3恢复、跨重启lastSong与收藏保持，新按钮明确拒绝未支持播放。UI/input79、AudioContent78、Visual71。游戏已正常退出，6份临时文件已恢复；见[整合证据](validation/foundation-return-menu-final-20261021/README.md)及[实机证据](validation/runtime-return-menu-20261021/README.md)。整体B01–B19仍未全部验收。
- **787/787、25套回归的菜单投影/独立LOCAL曲库整包已通过并安装**，18项安装hash匹配、268源码输入当时未变；Visual71、AudioContent78、UI/input77。当时发现整份资源元数据JSON超过日志上限，随后改为逐条输出并另建修正版；首包787不能代替后续789包的独立实测。最新可恢复现场见[检查点](../.docs/CURRENT-CHECKPOINT.md)。
- **B14 的历史投影阻断已由789包返回菜单实测补证**：778包改用精确 `GetComponentInParent(Type,bool)` 后仍被非等比视口守卫拒绝，后续四角投影和自建root X/Y补偿已进入789包，并取得上方返回窗口重建/选择恢复证据。早期[投影验证](validation/canvas-projection-20261021/README.md)保留；完整输入焦点/双包/手柄矩阵仍缺。
- **B15 新公共路径**：独立命名空间LOCAL目录、完整歌曲/难度/精确资源代次、原子发布、选择代次和保活请求均已接；32/128含退休持有配额、模块恢复/未知清理及正式scope撤回通过。原创WAV/PNG/chart已通过真实parser并发布两同标题不同ID歌曲。原生播放仍明确OperationUnsupported，不能将CPU目录称为可玩曲包。见[目录证据](validation/local-catalog-20261021/README.md)和[独立审查](validation/local-catalog-20261021/independent-review.md)。
- **B13 新实机证据单列**：767完整3分卷2732条中已取得材质退休completion receipt；778失败共存会话虽有4条旧panel断言错误，但两个真实插件对同一TMP字段取得白0→低1→高2→低1→白0的原生getter及holder序列。共存修复会话0Error、旧三层与TMP同时工作；最新Visual71另修未知free的有界责任隔离，不能据此宣称GPU/像素或所有视觉类型验收。

- **前一767/767、25个项目通过且已安装**，Visual69，其余检查继续通过；0编译警告/错误，18项安装hash一致、256项源码未变、三项原始游戏文件不变。补充视觉退休成功Info回执与日志异常不影响清理的回归，仅证明cleanup谓词完成和退休预约释放，不证明GPU或GC根最终释放。该包后续完整实机见[退休会话](validation/runtime-767-retirement-20261021/README.md)，不改写766的边界。见[新包验证](validation/retirement-receipt-20261021/README.md)。

- **766/766、25个项目通过且已安装并完成局部实测**；内容64、UI/input73、GameResources25，0编译警告；构建时18项安装哈希一致、256项源码输入未变，原生fixture31,261/61,358。内容事务/失败清理和模块旧消费者预算、TMP颜色共享目标与独立基线、手柄current身份及场景顺序已接。见[整合证据](validation/foundation-color-content-20261021/README.md)。后续原生退休成功receipt源码属于下一包，不用于补证766。
- **766本包独立实机已冻结**：[967条同session事件](validation/runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)证明Pause、暂停约21秒游戏时钟冻结而host/DSP推进、Resume后续恢复、Retry旧局Aborted及同谱新RunId、一次hold.end原调用返回和Perfect1→2，以及TMP真实发现/基线getter、Translucent Float应用/切场景托管lease释放。本次Error0、Warning13，含一次cleanup pending且根/预算保留；没有正常Completed、TMP覆盖/恢复、材质物理释放或时间单位映射证明。游戏已退出，临时probe[恢复原Absent状态](validation/runtime-color-content-20261021/module-probe-restoration.json)，旧失败启动与旧session均保留。
- **内容事务独立只读审查已完成**：[审查索引](validation/content-transaction-audit-20261021/independent-review.md)记录实际源码hash、统一事务/依赖保活/错误线程/模块预算路径，未发现可从正常API调用序列确认的新缺陷。该审查未重新运行64项用例，不覆盖WorkshopChartService全部异常释放路径、真实native free或完整B15曲包接入。
- **前一744/744、25个项目通过且已安装**；AudioContent52、UI/input70，18项安装哈希匹配、255项源码输入未变，原生fixture31,261/61,358通过。新增手柄16个数字按钮及独立设备失效、原子内容组与消费者依赖保活；真实游戏目录/音乐/封面边界已静态提取。一个测试代码可空警告保留，新包没有实机证据，完整任务未验收。见[整合报告](validation/foundation-batch-gamepad-20261021/README.md)。
- **前一整合包717/717、25个项目通过且已安装**；原生fixture 31,261 / 61,358，额外安装恢复13/13及安全13/13。Runtime57、Visual68、UI/input59；[18项安装哈希匹配](validation/foundation-admission-recovery-20261021/installation-check.json)、[252项源码输入未变](validation/foundation-admission-recovery-20261021/source-integrity-check.json)，构建无编译警告。新增有界窗口发现、动态准入和公平历史、采集/派发焦点与逐回调epoch校验、安装旧文件备份及失败恢复。详见[整合证据](validation/foundation-admission-recovery-20261021/README.md)。游戏保持关闭，本包尚无新实机；只有B03和B18a勾选。
- **前一批四项修复已全量构建并安装，691/691、25 个项目通过；新包尚待独立实机验收。** 证据集中于[本轮验证目录](validation/runtime-transition-fixes-20261020/)，[构建摘要](validation/runtime-transition-fixes-20261020/build-summary.json)记录 Game77、Hooks43、Visual58、Mutation21、Judgements35；[18项安装哈希匹配](validation/runtime-transition-fixes-20261020/installation-check.json)、[247项源码输入未变](validation/runtime-transition-fixes-20261020/source-integrity-check.json)。原生 fixture 31,261 / 61,358 项通过。Scope.Tests:92 有一条可空引用编译警告，已保留在摘要中。下列 666/666 是上一正式包的历史结果。四项分别是：①视觉发现过程中进入 `VisualCleanupPending` 后立即撤回可用状态，已有视图将其作为暂时不可用处理，并继续阻止新写入；②长按尾身份改为精确 Boolean `MoveNext` 入口捕获的真实协程上下文，不再借用可能被覆写的 RSI；③结算入口保留原调用来源，未知或旧局结果不借用当前同谱局身份；④目标 registry 共享线程安全单调代次，跨 registry 旧句柄不能命中新绑定，代次耗尽后拒绝注册。分别见[视觉回归](validation/runtime-visual-scope-cleanup-20261020/transition-regression-after.log)、[Hold 定位与修复](validation/runtime-transition-fixes-20261020/hold-identity-checkpoint.md)、[结果来源红](validation/runtime-transition-fixes-20261020/result-origin-regression-before.log)/[绿](validation/runtime-transition-fixes-20261020/result-origin-regression-after.log)及[B12 审计与实施检查点](validation/runtime-transition-fixes-20261020/visual-target-admission-audit.md)。这些改动不新增整项验收。
- 上一包完整离线构建为 **25个检查项目、666/666（Visual56、UI/input52、Hooks38、Judgements27）**，见[构建摘要](validation/runtime-visual-scope-cleanup-20261020/build-summary.json)。666已正式安装，[18项文件哈希匹配](validation/runtime-visual-scope-cleanup-20261020/installation-check.json)，[242项源码输入未变](validation/runtime-visual-scope-cleanup-20261020/source-integrity-check.json)。修复了正式停用插件时视觉资源清理使用已失效插件dispatcher的错误；正式PluginHost回归由55/56变为56/56。[273条新实机事件](validation/runtime-visual-scope-cleanup-20261020/README.md)验证两次真实Float创建/应用/正式停用及一次中间重新启用，管理scope/资源/服务视图两次归零，无Error。原生延迟清理Warning保留，没有最终物理释放receipt；旧665错误证据不改写。
- **665包已正式安装并实测**，18项文件哈希匹配、242项源码输入未变，见[集成证据](validation/runtime-judgement-identity-20261020/README.md)。[逐音符公开服务](Judgement-observation.md)已经接通原生调用者捕获、真实对象图、局内音符身份及作用域订阅；Prepare → Configure → Commit 发布与 broker 接线已实现。原生 ABI/展开 fixture 31,261项、真实固定 Dobby 发布 fixture 61,358项通过；这些检查不代表目标入口改写对并发执行线程是原子的。
- 665包[1,985条冻结事件](validation/runtime-judgement-identity-20261020/auto-retry-skip-proof.json)记录675次判定观察、675次原调用、未关联0，实际覆盖 **8/12来源**；保存17条有界SDK详细样本，其中自动头判定和长按尾判定共享真实音符ID。三局分别为 Failed、Retry Aborted、Skip Skipped；Retry/Skip不是主代理电脑操作记录中的动作。第一局479次判定与实际结果计数一致，但不能由此推断每次请求均被评分接受。后续正式停用 `example.panel` 产生1条真实 `ScopeGate.Dispose` 清理错误，666修复针对该错误；正常完成、四个未覆盖来源、偏差单位标定与长期GC等仍缺。
- **644包的输入和材质证据单列**：[469条前半会话](validation/runtime-input-capture-fix-20261020/runtime-proof.json)验证 after-input 绑定、键盘采样、SDK注册及两次 Translucent Float 创建/赋值、manual 与 scene-change 租约释放。用户实体F8的[561条所选会话](validation/runtime-input-capture-fix-20261020/physical-key-proof.json)保留前469条不变，证明一次采集 invocation147711/updateType1/updateCount147715 → Host排空一次 → SDK回调一次，native键盘时间推进。Unity延迟销毁时保留根和预算已观察；最终物理释放无独立receipt，完整输入矩阵仍缺。工具F8/Return无响应与第一次644包[输入初始化失败](validation/runtime-input-retirement-20261020/README.md)原样保留。[本地启动器](../tools/RizToolbox.GameLauncher/README.md)的8/8准备检查及宿主恢复独立于正式包。
- 前一 **607/607、24项目（Visual47）** 包，见[回归摘要](validation/runtime-material-slots-20261020/build-summary.json)。该包正式安装并核对17项实际文件哈希；[488条所选会话](validation/runtime-material-slots-20261020/runtime-proof.json)证明真实Canvas/BG Translucent槽的三项Float参数克隆创建/覆盖各两次，manual与scene-change Dispose后租约失效、探针保留计数归零。TMP Color、基线恢复receipt、可见像素/GPU及完整B13仍未验收。不同包不自动继承旧包全部实机结论。
- **603检查包的生命周期证据单列**：[1095条所选会话](validation/runtime-back-skip-20261020/runtime-proof.json)验证Back请求及匹配终止边沿→Aborted、SDK Skip精确返回后延迟派发→Skipped，两owner scope先于普通回调失效，新RunId不复用、无重复结束/ObservationLost/伪造Result。该包后来[材质拒绝证据](validation/runtime-back-skip-20261020/material-refusal.md)只验证旧目标及精确shader拒绝；新607包独立补到Float成功。构建摘要nativeAcceptance保留构建时状态，随后实机以各自会话为准。
- **上一582检查包的证据单列**：[实测摘要](validation/runtime-cancellation-20261020/runtime-proof.json)验证 SDK Pause 后游戏时钟冻结而 host/DSP 前进，Resume 的立即条件为 false、随后同局恢复推进，Retry 对旧局产生 VerifiedCallback/Aborted、新局不复用ID、两 owner scope 先取消、Retry 审计仍关联旧局。该会话的 Skip 当时只有返回 LevelList/ObservationLost；它没有被改写为成功，新603包的独立会话才补到 Skipped 终止。紧凑审计 JSON 已实际重测有效，有限投影不等于完整审计全字段通过。
- 更早的[按钮包冻结会话](validation/runtime-unified-20261020/native-button/evidence-summary.json)有按钮回调、8组音频加载/播放/停止、一次原始菜单导航进入 LevelList，以及同谱面 Retry 后新 RunId。该会话没有已验证结束，26条审计被截断；12:56额外点击只证明实际执行，不归属为全部由 agent 操作。失败证据原样保留。原始游戏二进制未修改。
- [本轮真实会话](validation/runtime-unified-20261020/lifecycle-probe-fixed/evidence-summary.json)证明两次失败结算、同谱面重试生成不同 RunId、第二局同 RunId 暂停/恢复，以及两个插件共四次单局 scope 取消/释放。判定 Hook 累计 959 次，原调用计数相同；三层颜色共同持有及优先级读回已验证。普通 UI 暂停/恢复没有进入原 NormalMode Pause/Resume hook，不能把安装成功当实际执行。完整 B01/B07/B08/B09 仍未验收。
- [不同目录源码重建](validation/foundation-unified-20261020/reproducibility.json)使用本机相同 .NET SDK、全新 obj/bin 与隔离依赖缓存，**7/7 个发布 DLL 哈希相同**。首次失败由自动嵌入未跟踪源码导致 PDB 差异；失败证据保留。它不是全新操作系统或游戏运行验收。
- SDK 编译引用包、作者模板及实际双包加载/启停/能力撤回已通过 10 项分发检查，见[作者指南](Authoring.md)。[性能脚本](Performance.md)自检 10/10，但尚无真实四基线、已校准预算或 20 轮场景证据。
- 更早的[双插件实机证据](validation/foundation-runtime-20261019/README.md)已证明存储迁移、同名文件隔离、语言热切、跨 ALC 可选服务调用/撤回，以及真实 444 谱面和 444 成绩跨页修订一致。这些证据只属于目录里记录的包。
- 更早的[单局证据](validation/B01-20261019-playback/)保留原始复现；现在已有真实失败、暂停恢复、Retry取消及 Back 提前返回/Skip终止的分包证据。正常完成和完整初始化/迟到结果矩阵仍待验。

用户恢复实测授权继续有效；999资源包实机已完整冻结，游戏进程已停止但未观察退出动作，marker和原创fixture已恢复。1007前置门源码完成构建暂存，尚未安装/生产接线。953及更早分包证据保持各自包/会话边界。正常完成、完整判定/单位、视觉、输入/手柄、双包与长期原生压力仍待验。

## 19 项任务逐项状态

**666后续失败证据保留，相关修复已落盘、实机待验**：[949事件](validation/runtime-visual-scope-cleanup-20261020/auto-return-proof.json)保留前273清理成功事件。游玩后返回LevelList仍AwaitingResult，无RunEnded/结果；479为内层Hook计数，最后身份快照仅281观察/8未关联。687的auto.challenge-max样本成功，689随后ReadHoldNote原生内存验证失败而撤回本局身份。固定游戏二进制已证明挑战长按尾路径会覆写 RSI，已改用独立协程调用上下文；未记录的当次寄存器值不作推断。929/931另有VisualCleanupPending使FrameUpdate回调失败并停止example.panel，模块随后恢复但插件仍Faulted；新的可用性切换修复不能改写原包这两条Error。结果来源修复阻止迟到结果误关新局，不能据此补造这次缺失的Result/RunEnded。四项修复及[B12仍存缺口](validation/runtime-transition-fixes-20261020/visual-target-admission-audit.md)不新增整项验收。

| 任务 | 已有实现与证据 | 尚缺的完整验收 |
|---|---|---|
| B01 单局闭环 | Game77；分包真实证据覆盖失败、暂停恢复、Retry Aborted、新ID与603包Back/Skip；665包三局分别Failed/Aborted/Skipped，失败结果关联实际开局，所选结算停留无重复结束 | 正常完成、结算页不重复/初始化/未开局/迟到结果完整矩阵；不能用场景进入或历史最佳补造开局 |
| B02 原生运行时 | 当前离线Runtime177；精确static/ctor/field setter及显式publication异常责任；846metadata、907参数/资源、953未激活clone和999双bundle撤回/GC/后帧销毁分别有实机 | 激活/发布后native消费者结束、长期IL2CPP GC/heap/GPU及完整异常矩阵仍缺；不从root记录推断heap归零 |
| **B03 SDK 契约兼容：已验收** | 保留 1.1 公共签名及固定程序集身份，结构化结果与版本范围；保留的真实旧插件经正式加载器和宿主运行，SDK 13 项。见 [兼容说明](SDK-compatibility.md) | 后续每批公开契约变更仍需运行兼容回归 |
| B04 包生命周期 | 可选依赖、冲突范围、排序、持久启用、迁移及回滚；Hosting 20、PackageLifecycle 14；本轮成功重启，未重放 disable 命令，提供者仍保持明确停用 | 双包依赖/冲突和升级归属矩阵 |
| B05 作用域/调度 | Scope30；run绑定session/run/scene/revision、迟到拒绝及提供者撤回；分包验证两owner scope先失效；666正式宿主父子视觉清理回归与两轮真实停用/中间启用通过，管理scope/资源/服务视图归零且无Error | 完整资源逆序释放、队列迟到/超时、双包压力、回调内停用及重入矩阵；管理清理不代表原生最终释放，未验证的Unity阶段不宣称支持 |
| B06 模块适配 | 独立清单/精确符号、依赖/代次、退避和局部失效；Module26；603包所选status中22模块active，Retry/Back/Skip能力available，设置override正确等待 | 完整真实故障隔离、能力撤回/恢复与旧服务失效矩阵；RuntimeProbed不是所有操作验收 |
| B07 精确 Hook | 当前离线Hooks189；18操作入口已纳入pre-init统一27处发布/V3，生产只读采用和多sink共享已接，真实游戏冷启动/metadata/callback仍缺；三类typed Operation观察和永久completion责任；Start及三处Retry/Back内部前置门/故障/worker/独占/cdecl回归通过，新policy未生产绑定。既有int32/single外层、enum32/single内层和native上下文/Prepare/Configure/Commit有独立分包实机；fixture31261/61358 | 新skip真实游戏、首次结果receiver归属、宿主长期门责任与完整来源/在途撤回、重入/异常/优先级矩阵；不宣称入口并发改写原子性，参数修改未开放 |
| B08 修改协调 | 优先级、变换、独占、外部基线、读回/回滚，Mutation21；颜色接入公共协调器，GameResources 18；本轮新增跨registry旧句柄不能覆盖新绑定的回归，已纳入717完整构建 | 新机制的实际多包交错启停、游戏外部写入及场景恢复；旧颜色栈实测不代替新机制 |
| B09 生命周期 | Game77；分包实机已有失败/暂停恢复/Retry取消与603包Back/Skip匹配终止边沿、scope先失效；665三局不同RunId与Failed/Aborted/Skipped；原调用结算来源保留、未知来源拒绝及同谱旧结果隔离有独立红绿日志；766补Pause/Resume、Retry旧局Aborted及同谱新RunId | 正常完成、初始化/中途启用/快速重复进入、真实同谱迟到结果origin及完整订阅矩阵；不能补造666所缺结果 |
| B10 修订数据 | Data 24；单次采集、不可变修订、分页、旧快照和取消。真实两包各读 444 谱面/444 成绩、同 session/revision 已验 | 当前局结果缓存及设置来源已接入统一构建；实际字段语义与真实流程仍待验证；不能把旧包 Unsupported 状态当新模块结果 |
| B11 玩法原语 | Gameplay30、审计17、Judgements35；公开逐音符服务、12固定调用点与真实Note/谱面图已接通；665实机675观察/未关联0、8/12来源、17详细样本及头尾相同ID，第一局479计数与失败结果一致；本轮Hold身份改用独立精确Boolean协程上下文，静态定位与回归已落盘，已纳入717完整构建 | 新包真实挑战尾部；665未覆盖四来源须完整新包验证，666单个auto.challenge-max样本不代替身份可靠性；手动Hold/长期GC/最终根释放、RawGameUnits标定、完整结果与控制矩阵、不同刷新率和速度仍缺；Speed/Seek未开放 |
| B12 语义目标 | 唯一/缺失/歧义、活跃/模板、别名/代次；Mutation21；跨registry代次隔离；原生4096窗口跨轮推进、满128语义刷新与安全单次轮换、50000历史边界；当前离线Visual71/Runtime160；历史Visual68及Runtime57，见[验证](validation/visual-admission-20261021/service-admission-checkpoint.md) | 新包动态场景/重建/地址与实例ID复用实机；任意数组重排、公平准入等待时长、全持有时延迟和语义别名恢复矩阵 |
| B13 视觉资源 | 当前离线Visual71、GameResources25；TMP_Text.color精确访问器及独立颜色通道已接，7项新增托管回归；attachment pins、有界延后清理和恢复失败保护；666前273事件两轮正式停用保留；pending可用性重判回归已通过；766真实TMP发现/基线getter、Float读回应用及场景托管lease释放 | 766仍有native cleanup pending，最终物理释放/基线恢复receipt未取得；TMP实际覆盖/双包恢复、动态准入实机、像素/GPU、逐类双包及音符/轨道/相机/后处理仍缺；后续退休receipt源码不能补证766 |
| B14 UI/输入/导航 | 当前离线UI/input79；直接current设备替换会撤销旧手柄queued/drained epoch，场景清理先于新设备采集；16个手柄数字按钮、独立设备状态和失效已接，专项11项；新增采集及派发双重焦点和逐回调owner/epoch校验；644真实 after-input 采集→有界队列→Host帧，用户实体F8一次采集/排空/回调且设备时间推进；分包overlay、文本编辑、自有按钮和菜单导航保留；disabled鼠标独立报告，自有按钮仍工作 | 实际手动更新/短按重复/重绑、双插件焦点/模态冲突、至少两个真实挂载点及停用恢复完整矩阵；真实手柄/热拔硬件矩阵未验；游戏输入截获/任意页面跳转未支持 |
| B15 音频/本地内容 | 当前离线AudioContent640；返回ABI2和三类tracker独立身份关联13组及实际新桥889/5受控provider联合已验证；四类engine节点传输、只读CLR桥/dispatcher与节点代次账本已实现，自有真实Dobby→CLR→实际账本855检查/5进程；固定UnityPlayer安全首次发布/生产接线仍缺；六Stop请求观察/共享Pending/Ready与根退休屏障已接，实际取消完成未证；12个监听变更入口源码接线、实际Coroutine身份和生产根采用故障回归通过；LOCAL目录/事务/revision续持/会话/结果claim与完整owned bundle已接；814wrapper、846metadata、907参数、953inactive clone、999双bundle来源撤回/原生GC/逐份退休分别有实机 | 参数初始化、激活一次/六回调归属、非空globals条件恢复/消费者结束、LOCAL result policy实际接线及结果/返回/新图重试仍缺；Workshop非空正向未验，953 globals只为null基线，RequestPlay仍OperationUnsupported；混音/可闻/延迟完整矩阵未验 |
| B16 存储/语言/设置 | Storage 14；真实双包 schema 迁移保留原件、同名 Data/Cache 隔离、语言键隔离/热切通过；新游戏音量 read/override 独立 Settings 25。见 [设置说明](Game-settings.md) | 新游戏设置已统一回归；真实设置基线恢复/外部写入/双包交错；未实现任意存档事务 |
| B17 跨插件服务 | DispatchProxy、共享身份/哈希、版本/代次、通知、大小/期限/循环控制，Services 19；真实独立双包调用及可选提供者撤回已验 | 实机提供者故障、硬依赖联动和完整升级/契约矩阵。见 [说明](Inter-plugin-services.md) |
| B18 诊断/性能 | **B18a 已验**：诊断 16、导出 9 及真实会话；工作树已加入有界 p50/p95/p99、回调耗时/线程分配、scope/process 资源采样，独立 Performance 11 | 采集/比较工具与 10 项脚本检查已有；四种运行基线、真实 20 轮切换、资源曲线/阈值及 CI。默认只观察，尚未校准的回调阈值不自动启用；宿主 tick 不是整帧/GPU 指标 |
| B19 分发维护 | 完整25/1617，另39自有早期加载进程/7316父断言；576源/22包冻结未安装，旧安装与原游戏hash一致。[证据](validation/foundation-early-loader-20261025/README.md) | 生产首次发布/V4、实际接线/退休、游戏流程、干净环境与升级完整矩阵仍缺 |

## 下一批收口

953已验证未激活controller创建/GC/销毁；907参数和更早证据保持原包边界。下一步组合完整owned play bundle，在提交前注册pending session、结果门和资源/globals baseline，完成参数注入/原版设置初始化，再实现受控激活、Awake一次与六回调归属、条件恢复/消费者结束强证明、真实结果、自有面板/返回及新图重试。见[本轮实机](validation/runtime-inactive-controller-20261022/README.md)和[检查点](../.docs/CURRENT-CHECKPOINT.md)。完整B01–B19不缩小，未验条目继续未勾选。
