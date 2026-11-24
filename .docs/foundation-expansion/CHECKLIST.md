---
plan: foundation-expansion
version: 1.1
created: 2026-10-19
updated: 2026-10-28
status: active
---

# Checklist: foundation-expansion

2026-10-30 当前续接：[Single V7 及同源码候选](../../docs/validation/foundation-single-v7-20261029/README.md)已冻结，V7 / 2528B / 26 operation / 64 writes / 83 EAT；25 套 2233 项、发行 14 项、验包 32 项、合成安装恢复 26 项实际通过。真实游戏仍按用户安排稍后，整项勾选未扩大。当前新树继续补订阅退出、游戏专用外观及采样时钟映射，不能用该候选验收后来源码。下方为各历史批次，不以旧版本字段覆盖本段。

2026-10-28 最新 source 批：唯一 GetInfoV6 / capability4 / 25 操作 / 56 写入，22 个实际源点、43 bindings 与重复 occurrence 转移、内联事件关联已接线；EngineTransport 独立保持 V5。894 Audio、28 协调器、10 身份、474 专项、508 CLR 采用、37 个映像场景实际通过，24/26 候选已打包核验。[证据](../../docs/validation/foundation-tween-sources-20261028/README.md)。19 项合成安装矩阵尚在跑；当前新子事件登记代码不在该冻结包。完整 B01–B19、实际游戏、全部 effects、动态对象同步保根、未来来源关闭和最终释放仍未验收，不增加整项勾选。下方各批保留历史范围。

2026-10-28 本批：实际 Boolean cached listener 已接入生产快照；仅参数变化也进入 graph equality。移除本轮旧 SDK 转换/旧二进制兼容矩阵及旧 listener shape 入口。当前 Audio/content 866/866、协调器 28/28、对象身份 10/10，六套专项 468/468、CLR 两模式 423 检查实际通过；六组件 release 与新 24/20 候选打包核验完成。没有安装或运行游戏。Tween/UI 非空效果 owner、未来来源关闭及生产首次准入仍未完成；不新增整项 B 验收勾选。

2026-10-27 当前：本地内容正式 play/source/result/return/retry 接线、设置变化订阅和 UnityEvent 按调用代次的效果档案已实现。最新功能快照 core-r2 实际通过 Audio/content 844/844、协调器 28/28、对象身份 10/10；core-r1 的七套相关专项 215 项另列，不混为最新全矩阵。完整生产首次准入、非空 listener 的独立效果 owner 与未来写者封口仍缺，真实游戏按用户安排暂缓。

用户最新安排优先于历史清单：**未发布阶段不做框架自身的向后兼容，按最新设计统一 SDK/API/协议/包；性能优化后置，先完成功能。** 当前验收要求同一候选合同一致、错误合同清楚拒绝；历史 SDK、签名、包或协议的运行兼容不再作为当前阻塞。不删除历史原件或改写测试结果。下面历史进度中的 B03 勾选仅保留原包已验收范围，不据此认定后续合同变更已重测。游戏侧版本、真实 ABI 和生命周期校验仍须保留。完整当前记录见 [PROGRESS.md](../PROGRESS.md)。

以下起为历史各批次记录；其中“当前”“下一步”以各批当时为准。

当前状态：[N 批普通 wait 责任](../../docs/validation/foundation-engine-wait-20261026/README.md)完成本批受控验证：完整 Audio/content 761/761、协调器 28/28、对象身份 10/10，自有原生 22/22，以及实际 Native→CLR wait 联合 8/8；同新核的旧 M 原生 16/16、联合 7/7 也通过。此前 [M 批 scheduler](../../docs/validation/foundation-engine-scheduler-20261026/README.md)、[L 批 Start 内部清理](../../docs/validation/foundation-engine-inline-20261026/README.md)及 [I 批生产候选](../../docs/validation/foundation-engine-production-20261025/README.md)保留各自历史证据。下一步将新增通道接入仍只发布四个 Node 入口的生产启动组件，并补真实重放、全部来源和最终完成合同。游戏仍未安装新代码，旧 r5 不含后续变更；生产 admission、原生取消完成、全部未来来源封口、全消费者 verifier 与完整资源退休仍未完成，B01–B19 验收状态不变。下方各批次为历史时点。

<!-- ENGINE-PUBLICATION-LATEST:BEGIN -->
最新：[V4 原生发布与同进程 CLR 联合验证](../../docs/validation/foundation-engine-publication-20261025/README.md)。实际完成 34 个主要进程场景：6 正常/托管、26 启动故障、2 动态加载拒绝；故障原件通过 2035 条离线断言，18 个有写入责任的终止场景由父观察器独立读取四个完整函数体及页保护。构建、7 组纯模型和 2549 条六例原件复核分别计数。本批未运行游戏，新候选未安装；生产 Doorstop→Bootstrap、只读采用/协调器及对象/GC/调度取消/资源收尾仍未完成。B01–B19 状态不变，仅 B03 整项和 B18a 子项正式验收；下一步见[施工表](../../docs/validation/foundation-engine-publication-20261025/next-production-admission-review.md)。
<!-- ENGINE-PUBLICATION-LATEST:END -->

以下是上一批的历史时点，后续进展以本页顶部和一页进度为准。

<!-- DOORSTOP-COLD-LATEST:BEGIN -->
最新补充：[实际Doorstop冷启动接线已验证](../../docs/validation/foundation-doorstop-cold-20261025/README.md)。session80976实际exit0：3正常冷启动+2错误部署拒绝、原21转发回归、旧默认Doorstop构建均通过。82名称/ordinal保持，候选真实DllEntry初始化先于自有Importer的TLS/C/C++/DllMain；626源/8二进制/585原件归档。固定游戏四引擎入口profile事实已补全。尚未安装或运行游戏，生产V4/新核只读采用、对象GC、调度、取消与全部资源退休仍待完成；B01–B19状态不变。
<!-- DOORSTOP-COLD-LATEST:END -->

以下为前一批独立转发组件的历史时点；其“尚未接入Doorstop”已由上述候选接续，仍未接入真实游戏。

<!-- WINHTTP-FORWARD-LATEST:BEGIN -->
最新补充：[固定系统 WinHTTP 转发已通过独立宿主验证](../../docs/validation/foundation-winhttp-forward-20261025/README.md)。root实际session40901/exit0，21进程、5653父核验；613声明源/4二进制/260原件归档。TLS与C/C++入口保留问题已修复，CFG保持。该组件尚未接入生产Doorstop或游戏；下一步是生产首次发布与新核采用，然后对象GC、调度、取消和全部消费者退休。此前1617属于旧完整构建，本批未重跑；游戏实测仍稍后，B01–B19验收状态不变。
<!-- WINHTTP-FORWARD-LATEST:END -->

以下新核启动链为本批之前的历史时点，其中关于WinHTTP尚未实现的描述已由上方接续。

<!-- EARLY-KERNEL-LATEST:BEGIN -->
- **最新原生集成：新 Kernel 自有启动链已接通。** [本批原件](../../docs/validation/foundation-early-kernel-20261025/README.md)。root 实际观察 session17459 终态0；新链73/13248断言、旧发布器57、旧链73/13031断言均通过，共203个自有宿主；604项声明输入和18条二进制按7+4+7隔离归档。本批没有重跑上一批25套1617，23项包字节保持。
- **实际修复与证据**：独立 publisher 记录2、wire3与helper ABI2/scalar1分层；复用同一核心代码，固定模块/入口/epoch不混用。调试观察改为有界映射身份与线程硬件返回断点；3个错栈经过保留责任、后续真实RSP返回清槽。正式debug为5Load/0Unload，另3次强化预飞实际各6Load/1Unload；1007未命中，不计覆盖。首批31803与失败诊断原件保留。
- **直接下一步**：实现固定系统WinHTTP转发及生产启动凭据，再接新核的CLR只读采用、coordinator/真实对象GC、scheduler/async/Start内联清理和全部消费者资源退休；补完整Retry/Return/Loader。新链仅用于自有宿主，真实游戏仍是旧安装，18安装项/4原始文件hash一致；未安装或运行游戏。B01–B19仍active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。
<!-- EARLY-KERNEL-LATEST:END -->

下方为上一批独立核完整构建的历史时点；其“下一步”已由上方接续，不代表当前固定链仍未实现。

<!-- ENGINE-KERNEL-LATEST:BEGIN -->
- **最新完整构建：独立协程运行核已实现并验证。** [本批证据](../../docs/validation/foundation-engine-kernel-20261025/README.md)。session23528实际exit0，25套1617/1617，599项声明源/23项包冻结。新Kernel仅39424字节、8个ABI2导出和2项系统导入，无入口/TLS/CRT/Dobby直接依赖；canonical入口表改为typed constexpr，旧helper继续独立回归。
- **实际范围**：新核7进程：128入口与256拒绝、1606宿主检查/1486父断言；原生3402+五托管889检查，180原调用/152配对。旧39早期宿主/7316另计。31项输入在新核运行期间持只读句柄且hash不变；后续另一测试正常复制旧helper的一项变化及准确旧文件恢复已单列。94条去重编译诊断保留，未声称零warning。
- **接下来**：新Kernel尚未由生产或固定早期链采用；下一批独立绑定它，再实现真实WinHTTP/首次发布与V4、coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休。新包未安装，游戏实测稍后。完整B01–B19仍active，仅B03整项/B18a子项验收；EngineCancelled=false、Pending保留、RequestPlay关闭。
- **安装补充已修复并实测**：[独立证据](../../docs/validation/foundation-kernel-install-20261025/README.md)。新23包在旧22项白名单下的真实拒绝已复现；精确加入Kernel后，自有临时目录16项安全检查和13项恢复检查通过。三脚本覆盖修复与原核心构建分列，现行599源码另有组合归档，其余596项不变；没有再次宣称重跑1617，也未操作真实游戏目录。<!-- KERNEL-INSTALL-SUPPLEMENT -->
<!-- ENGINE-KERNEL-LATEST:END -->

### 上一批固定发布早期链（历史）

<!-- EARLY-FIXED-LATEST:BEGIN -->
- **历史：无 Dobby 的固定发布早期链已接通，73 个独立进程通过。** [本批证据](../../docs/validation/foundation-early-fixed-20261025/README.md)。root 观察 session34351 实际 exit0，14 项输入/7 个二进制已冻结；全 Prepare→4 Configure→Bind→4 Commit/Ready 早于真实 TLS/C/C++/DllMain 首调用。正常86/worker92次原调用，各56观察配对，包含真实前缀副作用和原异常。
- **本轮修复与验证**：Capture Busy/错误现永久关闭原生准入，真实受控递归及后续捕获均不能误放行；独立未发布进程建立同 binary/RVA 的 SEH/C++ raw flags 基线。73 个进程含56内部故障、实际1字节部分写和17条调试补充观测；API结果注入、跨 child 基线和仪器化边界均单列。上次完整25套1617/1617仍属于99557，本批未重跑托管套件。
- **接下来**：实现实际游戏的系统 WinHTTP 转发与可信首次发布/独立 V4，再接 coordinator/对象GC、scheduler/async/Start内联清理和全消费者退休，补完整 Retry/Return/Loader。游戏实测稍后，本批未安装；B01–B19 继续，仅 B03 整项/B18a 子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。旧576/12声明源、已装18文件/326源ZIP和4项原游戏hash已重新核对。
<!-- EARLY-FIXED-LATEST:END -->

### 上一批独立固定组件（历史）

<!-- FIXED-PUBLISHER-LATEST:BEGIN -->
- **历史：固定四入口最小发布组件已实现，57个独立进程通过。** [本批证据](../../docs/validation/foundation-fixed-publisher-20261025/README.md)。12项组件输入/4二进制独立冻结，session5523实际exit0；已去掉该发布路径对Dobby、动态路由、堆和CRT日志的依赖，原函数桩重放真实非NOP前缀。34原调用、20观察配对、16异常捕获；57份实际页快照、2850个控制展开上下文均有独立核对，控制上下文不等于逐PC真实异常。
- **状态分列**：上批完整25套1617/1617与39个早期宿主属于session99557的576源/22包归档；本批未重跑这些托管套件，也未把最小组件接进早期loader。FF25尾部实际展开失败后改R11续接，SEH flags改用未修改真实基线精确对比，失败原件保留。56故障中3类API失败是受控注入，实际partial3字节另有原件。
- **仍缺**：无Dobby早期宿主的同链验证、并发首写/实际游戏启动点、系统WinHTTP转发/独立V4、生产coordinator和对象GC、scheduler/async/Start内联清理、全部消费者资源退休及完整Retry/Return/Loader。新组件未安装；完整B01–B19 active，仅B03整项/B18a子项验收，EngineCancelled=false、Pending保留、RequestPlay关闭。游戏实测稍后。
<!-- FIXED-PUBLISHER-LATEST:END -->

### 历史完整构建：旧早期宿主

<!-- EARLY-LOADER-LATEST:BEGIN -->
- **最新：自有早期加载宿主已实现并纳入完整构建。** [本批结果](../../docs/validation/foundation-early-loader-20261025/README.md)。25套1617/1617离线回归；另39个独立子进程、7316条父断言通过，两组计数分列。576项声明源码/22项包冻结，新包未安装。
- **本批证据**：真实静态依赖链，四目标发布早于Importer的TLS/C/C++/DllMain；normal86与worker92次原调用，实际嵌套/worker/EAX/原生异常及加载后Attach；迟到已有worker拒绝和24处API边界故障已验。额外debugger子进程观察helper/Dobby真实初始化返回，明确属于内存断点仪器化证据。原件合计324次原调用/168观察配对/24异常捕获，完整构建session99557实际exit0，保留94条去重原始编译诊断。
- **仍缺**：游戏的生产早期安装、系统WinHTTP转发及独立V4凭据、coordinator实际对象/GC接线、scheduler/async/Start内联清理和全消费者资源退休。Dobby的CRT堆、日志及未解间接路径仍不具备生产loader安全证明，下一段实现固定四入口最小发布器。若干拒绝项是受控expected/selector条件，24故障不等于Dobby内部OOM/写入中断。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19继续，仅B03整项/B18a子项验收。游戏实测稍后；旧1021安装18项/326旧源ZIP与原游戏3hash匹配。
<!-- EARLY-LOADER-LATEST:END -->

### 历史1617（返回ABI与协程身份）


- **历史：返回 ABI 与协程身份关联已验证，25 套 1617/1617 离线通过。** [本批说明](../../docs/validation/foundation-engine-binding-20261025/README.md)。修正 Execute 保留完整 uint32 EAX，返回 ABI2 拒绝采用旧 helper；Start/Release 仍保 AL。现有三类 tracker 的 receiver/slot/generation/Enumerator/延后 Coroutine handle 与 native owner、node/engine generation 分别关联，foreign 隔离/地址复用/有界溢出保留责任；Runtime177、Audio640、Hooks189，565 源/22 包冻结未安装。
- **实证范围**：自有原生3402检查、136原调用/124观察配对；真实 Dobby→CLR→实际 identity/node账本五进程889检查、44原调用/28context，scalar provider/pairs受控。13组身份回归中3组走三类实际tracker实现及受控backend，与Runtime正式字段投影7组synthetic metadata回归分开证明。full51261 exit0；94条去重原始编译诊断，未声称零warning或干净换机复现。交叉复核发现的foreign遮蔽下owned未知node漏记已修正，修正前48324仅保历史原件。
- **仍缺**：UnityPlayer四入口的生产安全首次发布/独立V4凭据和coordinator接线；调度器消费/销毁、async与Start内联清理、实际对象/GC、全部writer/delegate/future消费者、完整资源退休及Retry/Return/Loader。新研究实际找到独立责任，不能把pre-il2cpp_init或Release0当完成证明。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19仍active，仅B03整项/B18a子项勾。游戏实测暂缓，旧1021安装18项/其326源码ZIP与原游戏3hash匹配。


### 历史1597（节点观察与标量账本）


- **历史：引擎协程节点观察基础已实现，25 套 1597/1597 离线通过。** [本批说明](../../docs/validation/foundation-engine-node-20261025/README.md)。四种实际 native ABI 的专用传输、只读 scalar 桥/多观察者分发及 Adapter 节点代次账本已接；关联同步首执行，分别记录摘链、执行窗口和最后 Release，After 不回读可能已释放的节点。Runtime170、Audio627、Hooks189；561 源/22 包冻结未安装。
- **本批实证**：自有目标真实 Dobby 原生检查3191项（128 original、116观察、4旧call跨Attach、5释放node情形）；真实 Dobby→新CLR→实际账本五进程855检查/44原调用/28配对context。另增10组dispatcher合同与8组ledger回归。full11589 exit0，保留 111 条不同原始编译诊断，初次环境遗漏和测试编译失败日志保留。
- **仍缺**：四个 UnityPlayer 入口的独立安全首次发布/V4 双映像凭据及生产 coordinator/tracker 接线，实际引擎 owner/Enumerator/Coroutine关联、scheduler撤销和Start内联清理、全部writer/delegate leaves/future消费者封口、完整资源退休与Retry/Return/Loader。自有scalar/返回受控，未执行游戏；FinalCleanupObserved不等于EngineCancelled。EngineCancelled=false、Pending保留、RequestPlay关闭，B01–B19 active，仅B03整项/B18a子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1579（操作入口预初始化）


- **历史：18 个新增操作入口已改为启动前统一安装，25 套 1579/1579 离线通过。** [本批说明](../../docs/validation/foundation-operation-preinit-20261024/README.md)。12 个监听变化、6 个 Stop 与既有入口统一准备并发布 27 处；生产 CLR 严格要求完整 V3 身份，只 Attach，不在游戏初始化后补装。这解决源码中的晚安装路径，真实游戏冷启动仍待验。546 源/22 包冻结未安装，Hooks189、Audio619、Runtime160。
- **本批实证**：固定游戏映像六进程 5074 检查，全部 18 tuple 与篡改拒绝；同映像生产 CLR 只读采用 384 检查/两进程。自有真实 Dobby 新传输 1477 检查（84 原调用、含跨 Attach 旧调用），新桥→生产 broker 五进程 837 检查/75 原调用。元数据受控，两类测试不拼成真实游戏链。full94416 exit0，保留 111 条去重原始编译诊断。
- **仍缺**：真实冷启动和 18 入口活体 metadata/调用、并发首发及任意部分发布故障动态验证；引擎 node 登记/执行/最终清理（已找到 UnityPlayer 静态链）、全部 writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整 Retry/Return/Loader。EngineCancelled=false、Pending 保留、RequestPlay 关闭。B01–B19 active，仅 B03 整项/B18a 子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1575（取消请求与退休屏障）


- **历史：取消请求与退休屏障已接入，25套1575/1575离线通过。** [本批说明](../../docs/validation/foundation-cancellation-request-20261024/README.md)。六个Stop声明typed观察关联现有owner/receiver/enum/handle/代次；独立记录请求、原函数返回和异常。共享Pending在provider前发布，阻Ready/Commit及三类协程后续根回收；不生成引擎取消完成。Audio619、Hooks185、Runtime160；534源/22包冻结未安装。
- **本批实证**：自有Dobby实际Prepare/Configure/Commit 1165检查、69观察+1未发布原调用；真实native→CLR→实际mutation/cancellation账本五进程544检查。图/根/provider/初始idle受控，实际引擎Stop与IL2CPP未执行；不是游戏并发首写安全证明。新增8组请求、6组Ready发布、8组退休屏障；full45961 exit0，保留94条去重原始编译诊断。
- **仍缺**：18个新增操作入口的真实metadata/安全首发与冷启动、scheduler登记撤销和取消完成、全部writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整Retry/Return/Loader。EngineCancelled=false、Pending保留、RequestPlay关闭。完整B01–B19 active，仅B03整项/B18a子项勾选；实测稍后。旧1021安装18项/其326源归档与原游戏3hash匹配。


### 历史1553（监听变更与取消身份）


- **历史：监听操作观察与协程身份已接入，25套1553/1553离线通过。** [本批说明](../../docs/validation/foundation-listener-mutation-20261024/README.md)。生产源码接12个固定操作入口；添加后移除恢复同图仍记录独立代次。实际Coroutine句柄与Enumerator/owner/receiver/槽代次关联。Hooks185、Audio597、Runtime160；522源/22包冻结未安装。
- **实证分层**：native Operation1156检查/72原调用；真实native→CLR bridge/broker五进程259检查/18原调用，均为自有direct-entry目标，无Dobby首写/游戏证明。10组生产CaptureDispatch根采用故障、8组实际mutation账本、6组协程身份与8组broker回归分别通过；账本图/根仍受控，Operation CLR没有串实际ledger。full60161 exit0，保留94条去重原始编译诊断。
- **仍缺**：新操作入口真实metadata/安全首发及游戏冷启动、序列化/泛型/直接写者和全部delegate leaves、Stop请求与取消完成、全部future和消费者资源退休、完整Retry/Return/Loader。MutationClosed=false、Pending保留，RequestPlay关闭；完整B01–B19 active，仅B03整项/B18a子项勾选。实测稍后，旧1021安装18项/其326源归档与原游戏3文件仍匹配。


### 历史1521（监听回调与账本）


- **历史：listener 回调与账本已连通，离线1521/1521、25套通过。** [证据](../../docs/validation/foundation-listener-clr-20261024/README.md)：502源/22包冻结未安装，Runtime160/Hooks177/Audio573。真实自有native→CLR bridge→生产共享broker core，normal/SEH/C++三场景进入实际ledger；图和根仍controlled。
- **本批实证**：12独立进程1630检查，覆盖三site、双sink、异常、target-read AV无Before的Abnormal(token0)、旧call跨Attach、detach/GC、worker、same/cross-slot nested、foreign-original和观察故障。rawAV不经过broker；非固定游戏receipt回调，非IL2CPP/GC或全部delegate leaves证明。full session21231 exit0，保留111条去重原始编译诊断（含ANSI变体及既有warning）。
- **余项**：真实固定游戏receipt callbacks/冷启动、动态leaves与mutation/provider转交和取消、全部future及消费者资源退休、完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。游戏仍1021安装18项/其326源归档与原游戏三文件不变。


### 历史1515（监听发布与只读采用）


- **历史：生产监听发布与只读采用已通过离线验证。** [证据](../../docs/validation/foundation-listener-publication-20261024/README.md)：25 套 1515/1515，493 源/22 包冻结，未安装。固定映像九处发布、V2 416 字节记录与 C# RequireReady 已接通，helper 共42 exports。
- **本批实证**：映像5进程1943检查，含 source/post/relay/chain 四种篡改拒绝；atomic Unknown713检查、V1/V2各100次确定竞态读取；CLR只读采用72检查/2进程。全部不执行游戏代码，listener未附加且调用0；上批自有native listener8349检查继续回归。full session 53963 exit0，保留111条去重编译诊断。
- **余项**：真实游戏冷启动、listener→CLR 回调与 ledger、IL2CPP/GC、动态 delegate leaves、连续 mutation/provider 转交与取消、全消费者退休和完整 Retry/Return/Loader 仍未验收。RequestPlay 关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。完整 B01–B19 继续，仅 B03 整项与 B18a 子项已勾；游戏实测按用户安排暂缓。 当前游戏仍1021安装18项/其326源归档，原游戏三文件未改。


### 历史1515（监听原生层）

- **历史listener原生层批：1515/1515、25套、491源/22包冻结未安装。** [本轮证据](../../docs/validation/foundation-listener-native-20261024/README.md)：自有native pre-tail/post-continuation 8349项，helper共41exports（新增5）；原硬件返回与动态chain/replay按实际fixture验证，非游戏六区发布。
- **边界**：新listener native only，非CLRlistener/ledger；独立lane非聚合V2，TargetReadFaulted After可无Before，V1Ready144不授权六区；invokable不等于每个delegate leaf。未验分支保留。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；session 45158 exit0，完整构建保留92条去重编译诊断，不宣称零warning或干净环境B19全矩阵复现。
- **余项**：本批listener为native only，不是CLR listener/managed ledger接入；没有固定游戏六区发布，不升级FirstInstall V1 Ready144。invokable dispatch不等于每个delegate leaf；真实游戏receipt callbacks、冷启动、IL2CPP/GC、连续mutation/provider转交/取消、全部future来源与消费者退休、完整Retry/Return/Loader仍缺。 RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。 游戏仍1021安装18项/其326源归档。


### 历史1515（采用回调联合批）

- **历史采用回调联合批：1515/1515、25套、484源/22包冻结未安装。** [本轮证据](../../docs/validation/foundation-adopted-broker-20261024/README.md)：同生产采用/dispatch core在自有native fixture真实回调379项/3进程，factory1、broker安装0；不是固定游戏receipt callbacks，metadata受控。
- **实证与边界**：happy123/fault181/late75；双sink/token、旧call跨Attach无新broker交付、原SEH/C++后恢复、stickyUnknown和重绑后GC仍可回调已验。未单计raw CLR入口；Info56不读回mid，生产postVerify失败仅源码复核；Runtime160/Hooks171/Visual71/UIinput79/AudioContent573。最终session 11559 exit0；完整构建保留109条去重编译诊断，原文见build-summary；不宣称零warning或干净环境B19复现。
- **余项**：真实固定游戏receipt的native→CLR回调、Doorstop冷启动顺序、实际IL2CPP metadata/listener/GC、未来来源与连续mutation/provider转交/取消、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。 实测稍后，游戏仍1021安装18项/其326源归档；完整B01–B19 active，仅B03整项/B18a子项勾选。

### 历史1515（CLR启动采用批）

历史CLR启动采用批[1515/1515、25套](../../docs/validation/foundation-clr-preinit-20261024/README.md)已冻结482源/22包，未安装。实际RequireReady/RuntimeAdopt与双idle sinks接入通过，native/game callbacks为0且metadata受控；atomic Unknown确定100读、生产C init受控ready16进程透明性通过。仅B03/B18a勾选，RequestPlay关闭，B01–B19 active，实测稍后；真实冷启动、listener/GC与全部退休仍缺。

历史1515（原生启动安装批）[1515/1515、25 套（Runtime160、Hooks171、Audio573）](../../docs/validation/foundation-preinit-producer-20261024/README.md)已冻结474源/22包，未安装。生产pre-init publisher及托管采用路径已实现，真实映像175断言验证三处发布但不执行游戏代码；真实CLR同进程采用、冷启、listener/GC与全run退休仍缺。RequestPlay关闭，B01–B19 active，仅B03整项/B18a子项勾选，实测稍后。

历史[1511/1511、25套；Runtime160/Hooks167/Audio573](../../docs/validation/foundation-first-install-20261024/README.md)已冻结暂存未安装；native observer每次调用snapshot、既有slot托管采用、原生698断言/2旧+18新及CLR联合通过。453源/18暂存；实际pre-init producer、首次安全发布、broker采用与完整闭合仍缺，游戏仍1021。B01–B19 active，仅B03/B18a勾选，RequestPlay关闭，实测稍后。

历史[1507/1507、25套；Runtime159/Hooks164/Audio573](../../docs/validation/foundation-void-admission-20261024/README.md)已冻结暂存未安装；新增准入屏障、实际CLR停车/释放与11组受控协议。445源/18暂存，独立native 656断言/15原调用；首patch及生产mid未证明，游戏仍1021。完整B01–B19 active，仅B03/B18a勾选，RequestPlay关闭，实测稍后。

历史[1496/1496、25套](../../docs/validation/foundation-event-mid-parent-20261024/README.md)已冻结暂存未安装，Runtime159/Hooks153/Audio573，新增6组真实Dobby父调用→mid→bridge/broker/ledger联合回归，包含干净owner成功采用后SEH/C++原异常。440源/18暂存，较1490新增1源；原生mid3559继续通过。图/root仍controlled，生产mid并发安全发布、真实IL2CPP/listener、全消费者退休和完整Retry/Return/Loader仍缺。RequestPlay关闭，仅B03整项/B18a子项勾选；B01–B19 active，用户“实测稍后”保持，旧安装/原游戏hash不变。

历史[1393/1393、25套](../../docs/validation/foundation-active-destruction-20261023/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio516，新增26组（12销毁+14事件）。实际factory五对象的独立销毁观察与Cutout EventSource已在激活前捕获，Ready/销毁阶段帧泵推进；required Cutout完整退出后须更晚真实帧核controller/GO/Transform dead，staging只对自己的空/未激活对象Destroy一次再等后帧。实际EventTrigger/GO/Transform、32上限triggers/UnityEvent/offset持根，StartEvents真实probe与计数纳入共享屏障，expected-dead保closure。EventSource仍Pending，分派后UnsealedDispatchSource=true；两类外置延迟及动态listener尚未闭合，五对象dead不代表音频图/globals/menu/全部回调结束。全active verifier、资源/协调器退休与完整Retry/Return/Loader仍缺，RequestPlay关闭。416源/18暂存冻结，较1367新增7/改3/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态16范围/1399指令及6直接E8候选字节核对，完整增量0编译诊断，定向17条warning保留（15既有、2新增测试计数器CS0649）。三个子代理实现/独立有限审查，新增26组受控检查；生产Unity Destroy/free、source get_Item quota故障及新backend游戏尚未直接验证。仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-active-destruction-20261023/next-integration.md)。

历史[1367/1367、25套](../../docs/validation/foundation-terminal-recycling-20261023/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio490，新增7组。已自然结束的Cutout/Fade观察根按撤身份→redrain→独立free→确认后复用记录，16条限制为同时尚未退休记录；累计计数/required Cutout事实不随复用丢失。Ready帧泵接回收与初始化观察者清理，读取activity期间worker故障在读后再次拒绝。409源/18暂存、旧1021安装18项/旧326归档/三原游戏hash匹配；12范围/991指令和6直接call候选字节再核对。完整增量0编译诊断，前置15既有warning保留。三个子代理有限审查；新增故障仅controlled backend，生产free故障及游戏尚未验收。发现Cutout通过另一个EventTrigger启动延迟协程/UnityEvent，必须单独封口；完整active退休/Retry/Return/Loader仍缺，RequestPlay关闭，仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-terminal-recycling-20261023/next-integration.md)。

历史[1360/1360、25套](../../docs/validation/foundation-terminal-coroutines-20261023/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio483，新增17组。named String Start/Factory ref0与Cutout/Fade tracker/backend已接coordinator/reservation/actual result capture窗口，缺对应发布不缓存假成功；真实Dobby及15组Audio/1Hook通过。静态10范围/957指令、三输入hash与6个直接E8候选再核对。408源/18暂存、旧1021安装18项/旧326归档/三原游戏hash匹配；完整增量0诊断，前置15条既有warning保留。terminal累计16记录的活动根尚未回收，Destroy请求非实际失效，ObservedEnded非全run结束；active图/全future退出、资源退休、Retry/Return/Loader仍缺，新backend尚无游戏验收。RequestPlay关闭，仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-terminal-coroutines-20261023/next-integration.md)。以下1343及更早是历史包。

历史[1343/1343、25套](../../docs/validation/foundation-initialization-coroutines-20261023/README.md)已冻结暂存未安装，Runtime138/Hooks126/Audio468，新增19组。真实初始化两协程tracker/backend接生产commit/menu返回前核对，实际controller/caller/native getter original窗口/rooted enum及delegate同时要求，Delay state1/2支持。15组tracker+3组合+1Hook、增强真实Dobby窗口检查通过，静态13范围/1731指令及三输入hash/常量再核对。405源/18暂存匹配，旧1021安装18项/旧326归档和原游戏三hash不变，15条既有warning保留。新backend尚无游戏验收，Loader/完整Retry/Return/全活动退休仍缺；RequestPlay关闭，只勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-initialization-coroutines-20261023/next-integration.md)。以下1324及更早是各自历史包。

历史[1324/1324、25套](../../docs/validation/foundation-coroutine-boundaries-20261023/README.md)已冻结暂存未安装，Runtime138/Hooks125/Audio450，新增9组。协程Start/MoveNext原生输送与public Start/Auto resolver、真实Dobby精确caller/tail relay/同步首Move/raw返回/worker/SEH/C++和8组Hook回归通过。9范围/1574指令及三输入hash/3.1与3.0常量再核对。402源/18发布归档匹配，旧1021安装18项/旧326归档及原游戏三hash不变，15条既有测试warning保留。尚无Adapter tracker调用点；实际enum/callback归属、getter隐式normal信用、全活动退休与完整Retry/Return/Loader仍缺，RequestPlay关闭。[具体下一接线](../../docs/validation/foundation-coroutine-boundaries-20261023/next-integration.md)。用户“实测稍后”保持，仍只勾B03/B18a；以下1315及更早均为历史包。

历史[1315/1315、25套](../../docs/validation/foundation-menu-handoff-20261023/README.md)已冻结暂存未安装，Runtime137/Hooks117/Audio450，新增17组。菜单显式normal/back信用及相机/粒子归属账本、native backend和启动类内接线已实现；ABA撤权、后继保留、全线程probe及旧Before→retain兼容已有受控检查，10范围/282指令静态再核对。395源/18发布归档匹配，旧1021安装18项/旧326归档与三原游戏hash不变；15条既有测试warning保留。菜单四入口无Dobby/game/unwind验收，getter隐式信用/旧回调、活动退休和完整Retry/Return/Loader组合仍缺，相同GO别名未适配；RequestPlay关闭，用户“实测稍后”保持，仍只勾B03/B18a。

历史[1298/1298、25套](../../docs/validation/foundation-judge-drain-20261023/README.md)已冻结暂存未安装，Runtime137/Hooks113/Audio437，新增11组。helper GetStats与inner Judge/Hold计数纳入LOCAL准入/终止快照/撤身份前后drain/停止，unknown与故障sticky。真实Dobby/C++fixture证明阻塞native worker和logical detach时仍在途、normal/SEH/C++退出归零，非managed inner/Hold异常跨CLR或游戏验收。390源/18发布归档匹配，旧1021安装18项/旧326归档及原游戏三hash不变。菜单/锁账本、全旧来源结束、active消费者退休、完整Retry/Return/Loader组合当时仍缺，RequestPlay关闭；只保留B03/B18a，用户“实测稍后”保持。

历史[1287/1287、25套](../../docs/validation/foundation-return-primitives-20261023/README.md)已冻结暂存未安装，Runtime136/Hooks107/Audio433，新增21组。内部void(Boolean)精确观察与一次返回场景owner已有生产实现；返回来源取同局实际已完成首次转场，必须观察自己的operation、目标loaded和原Level卸载。387源/18发布归档匹配，旧1021安装18项/旧326归档与三原游戏hash不变。新Boolean仅受控engine/calli检查，非真实Dobby/game或native original unwind证明；返回receipt不证明全部消费者结束。菜单/锁账本、完整native Judge在途、active退休、完整Retry/Return命令与Loader组合当时仍缺；RequestPlay继续关闭。用户“实测稍后”保持，只保留B03/B18a勾选。

历史[1266/1266、25套](../../docs/validation/foundation-actual-launch-20261023/README.md)已冻结暂存未安装，Runtime136/Hooks99/Audio420。生产coordinator已串联完整bundle/四池预留/inactive clone/实际setter/单次activation/Awake/六globals/首次scene transition，host帧泵等真实转场及初始化图核验后commit；设置前后回执与精确结果UI sink已实现。383源/18发布分别归档匹配，旧1021安装18项/旧326归档及三原游戏hash不变；转场7范围/1895指令再次核对。完整增量0编译诊断，前置Audio14条FakeNative CS0649及1条测试CS8602保留；没有本包独立ABI runner或游戏验收。菜单相机/粒子/输入锁、active消费者/资源/coordinator退休、完整Retry/Return/新局建图和Loader组合根当时仍缺，RequestPlay继续关闭。用户“先继续代码，实测稍后”保持，无新安装/UI/游戏输入；只保留B03/B18a勾选，不由检查数量推断B01–B19完成。

历史[1216/1216、25套](../../docs/validation/foundation-initialization-native-unwind-20261023/README.md)已冻结暂存未安装，Runtime131/Hooks99/Audio375。Start/getter生产original移至typed native helper，Prepare→Configure→Commit及finally清理/真实drain已接；新增native helper1747检查、生产普通/LOCAL的SEH/C++八种路径及同包独立runner通过。graph record两枚身份与固定publication槽已在native尝试前预留，3组发布回归通过；不扩大为整个流程零分配/完整I04验收。371源/18发布分别归档，旧1021实际安装18项/旧326源归档与原游戏三hash匹配。完整增量0诊断，前置Audio13条共享FakeNative CS0649保留。Play/Progress/Timeline原异常展开、controller map撤回/redrain/复用组合尚缺同等新fixture；当时实际launch/activation、settings/Awake/globals、commands/result view、Retry/Back与active退休仍缺，RequestPlay继续关闭。用户“实测稍后”保持，无新安装/UI/游戏输入；只保留B03/B18a勾选，不由检查数量推断B01–B19完成。

历史[1210/1210、25套](../../docs/validation/foundation-initialization-isolation-20261023/README.md)已冻结暂存未安装，Runtime131/Hooks96/Audio372；364源输入/18发布文件分别归档核对，旧1021实际18安装/旧326输入归档及三原游戏文件分别匹配。Start/getter/Play/Tooltip可调用生产实现与离线接线已补；完整Runtime及同包独立真实Dobby ABI runner通过，游戏未验。完整日志13条FakeNative CS0649保留，前置编译/旧retry断言失败保留。当前src无Loader/模块实际launch调用点，RequestPlay仍关闭；I04全部发布存储预留、actual activation、完整commands/result view、六globals、active退休与Save/Steam间接全覆盖未完成。用户“先继续代码，实测稍后”保持，只有B03/B18a已勾。本段不增加勾选；[1187](../../docs/validation/foundation-launch-reservations-20261023/README.md)、[1148](../../docs/validation/foundation-local-termination-20261023/README.md)及以下均按各自冻结包/会话理解。

历史[1081/1081、25套](../../docs/validation/foundation-local-parameter-binding-20261022/README.md)已暂存未安装，Runtime130/Hooks69/Audio271，新增43组参数/结果/有界读取检查。18当时暂存/337输入与18旧1021安装/326归档分别匹配，三个原游戏文件不变。真实参数setter和单次ControllerOver组件已有代码，该时完整激活/终止handler和native消费者结束仍未接；用户要求实测稍后，无新游戏验收。只有B03/B18a保持勾选。以下1038及更早为历史包边界。

历史[1038/1038、25套](../../docs/validation/foundation-inactive-publication-20261022/README.md)已构建暂存、未安装；现有安装为[1021保护宿主](../../docs/validation/foundation-local-protection-host-20261022/README.md)。新增17组交接/配额失败回归，Audio243/Runtime115/Hooks69；18暂存/329当前输入与18旧安装/326归档分别匹配，三原游戏文件不变。用户要求先代码、实测稍后；四门会话只有普通ControllerRetry正向，结果页三项待验，1280条0Error是未停止会话前缀。新8根工厂与bundle输入责任尚无本批游戏实测。RequestPlay仍关闭，不增加勾选。以下1007及更早均为当时的历史包状态。

历史源码[1007/1007、25套](../../docs/validation/foundation-local-command-gates-20261022/README.md)已构建暂存，**未安装，游戏仍999**。Hooks69新增三命令前置门及cdecl/故障回归；新门未生产接线/实机，不代表LOCAL激活完成。18暂存/322输入及当前999安装18hash分别核对通过；15条测试stub CS0649警告保留。下一[激活事务计划](../../docs/validation/local-launch-20261022/next-activation-transaction.md)已写，完整B01–B19仍active，无新勾选。

历史[999/999完整构建](../../docs/validation/foundation-local-bundle-20261022/README.md)与[879事件资源包实测](../../docs/validation/runtime-local-bundle-20261022/README.md)：Runtime115/Hooks61/AudioContent212，18安装hash/320源码与原游戏三文件匹配。两份独立native资源图在来源撤回/GC后可读，逐份后帧销毁及CPU计数2→1→0通过；0Error/7Warning完整保留。只确认进程停止，未观察正常退出操作；marker和原创fixture已恢复。LOCAL激活、设置初始化、首次结果receiver归属和Start/Retry/Back保护实际接线、globals发布恢复/消费者结束与完整游玩仍缺。不增加勾选，完整B01–B19继续，仅B03/B18a已完成。下面953等为历史包记录。

历史953/953、25套完整构建安装，Runtime98/Hooks61/AudioContent183；收尾18安装hash/303源码匹配、原游戏三文件不变。完整增量日志0诊断，前置定向15条FakeNative CS0649警告保留。[构建](../../docs/validation/foundation-inactive-controller-20261022/README.md)/[775事件实机](../../docs/validation/runtime-inactive-controller-20261022/README.md)：746Info/25Debug/4Warning/0Error；普通卡片精确发现、inactive parent下clone、Awake0/null globals不变、原生GC读回、后帧销毁与7root及观测根释放通过。两次数组协变误拒失败独立保留；修复后resolver14回归。游戏正常退出、marker恢复Absent。仍缺完整bundle/参数初始化、激活/非空回调发布恢复、结果保护实际接线/消费者结束/完整游玩；RequestPlay仍OperationUnsupported。完整B01–B19继续，只勾B03/B18a。
勾选必须说明对应 [SPEC](SPEC.md) 和证据包的验收范围。**2026-10-23 历史核对：当时 19 个整项中，仅 B03 完整验收；B18a 是已完成的子任务。该结论只适用于当时合同与原包，后续当前合同变更不继承旧兼容验收。** 各任务的实际实现、测试和缺口以 [实现状态](../../docs/IMPLEMENTATION-STATUS.md) 为准，不用测试数量计算覆盖率。下面历史记录只证明对应包与会话。

**历史766包构建/安装记录：766/766、25项目，0编译警告；18项安装哈希一致、256项源码输入未变。** 内容64、输入73、颜色目标25；失败清理/依赖保留/模块恢复配额、手柄设备身份及TMP颜色已接。首次尝试曾停在Steam登录，后续用户登录后完成有限实机，见[实机证据](../../docs/validation/runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/README.md)；[原构建证据](../../docs/validation/foundation-color-content-20261021/README.md)保留，不新增勾选。

**前一744包已构建并安装：744/744、25个项目，18项安装哈希匹配、255项源码输入未变。** UI/input70、AudioContent52；手柄数字边沿和原子内容组登记已实现，真实目录/音乐/封面静态边界已保存。测试代码1条可空警告保留。新包尚无实机，不新增勾选。见[本轮证据](../../docs/validation/foundation-batch-gamepad-20261021/README.md)。

**前一717包已构建并安装：717/717、25个项目，额外安装恢复13/13与安全13/13，18项安装哈希匹配、252项源码输入未变。** Runtime57、Visual68、UI/input59；动态发现/准入、焦点双检和逐回调代次、安装事务恢复均已合入。[整合证据](../../docs/validation/foundation-admission-recovery-20261021/README.md)。新包尚无独立实机，不新增勾选。

**前一批四项修复已构建并安装：691/691、25个项目通过，18项安装哈希匹配、247项源码输入未变；新包实机验收仍待完成。** 见[本轮构建摘要](../../docs/validation/runtime-transition-fixes-20261020/build-summary.json)、[安装核对](../../docs/validation/runtime-transition-fixes-20261020/installation-check.json)。Game77、Hooks43、Visual58、Mutation21、Judgements35；Scope.Tests:92的一条可空引用警告保留。 ①视觉发现后重新核对cleanup pending，已有视图按暂时不可用处理并拒绝新写入；②Hold身份来自精确Boolean `MoveNext`入口的真实协程上下文，不再依赖可能被覆写的RSI；③结算保留原调用来源，拒绝未知/旧局结果借用当前同谱局身份；④registry共享CAS单调代次，跨实例旧句柄隔离、溢出拒绝且不转移资源所有权。见[本轮验证目录](../../docs/validation/runtime-transition-fixes-20261020/)、[Hold检查点](../../docs/validation/runtime-transition-fixes-20261020/hold-identity-checkpoint.md)、[结果来源红](../../docs/validation/runtime-transition-fixes-20261020/result-origin-regression-before.log)/[绿](../../docs/validation/runtime-transition-fixes-20261020/result-origin-regression-after.log)、[视觉回归](../../docs/validation/runtime-visual-scope-cleanup-20261020/transition-regression-after.log)及[B12审计](../../docs/validation/runtime-transition-fixes-20261020/visual-target-admission-audit.md)。该历史审计的动态准入、metadata及窗口扫描缺口已在717实现并离线验证；实机仍待验，没有新增勾选。

上一包完整离线构建为 **666/666、25个检查项目（Visual56、UI/input52、Hooks38、Judgements27）**，见[摘要](../../docs/validation/runtime-visual-scope-cleanup-20261020/build-summary.json)。666已正式安装，[18项文件哈希匹配](../../docs/validation/runtime-visual-scope-cleanup-20261020/installation-check.json)、[242源码输入未变](../../docs/validation/runtime-visual-scope-cleanup-20261020/source-integrity-check.json)。665实机ScopeGate清理错误已修复，正式宿主回归由55/56变为56/56；[273事件实机](../../docs/validation/runtime-visual-scope-cleanup-20261020/README.md)验证两次真实Float创建/覆盖/正式停用、一次中间启用，管理scope/资源/视图两次归零、Error0。native cleanup pending保留，无最终物理释放receipt；各整项仍按原SPEC验收，未新增勾选。

**665包已正式安装并实测**，见[集成证据](../../docs/validation/runtime-judgement-identity-20261020/README.md)：18项安装哈希匹配、242源码输入未变；原生helper及Prepare/Configure/Commit与broker、公开逐音符服务已接通，原生fixture31261/61358项通过。[1985条冻结事件](../../docs/validation/runtime-judgement-identity-20261020/auto-retry-skip-proof.json)记录675观察/675原调用、未关联0、实际8/12来源、17条有界SDK样本及头尾共享音符ID；三局Failed/Aborted/Skipped，后两步不宣称由主代理操作。1条真实停用清理错误原样保留，由666另行修复验证；正常完成和剩余四来源未验。

**644包证据单列**：[469条前半会话](../../docs/validation/runtime-input-capture-fix-20261020/runtime-proof.json)证明after-input绑定、键盘采样/注册及两次Float创建/赋值、manual/scene-change租约释放。实体F8的[561条会话](../../docs/validation/runtime-input-capture-fix-20261020/physical-key-proof.json)证明一次采集→Host排空→SDK回调，保留前469条不变；该次updateType1，不代表完整输入矩阵。原生延迟销毁保留根/预算已观察，最终物理释放无独立receipt。

前一 **607/607、24个检查项目（Visual47）** 包见[摘要](../../docs/validation/runtime-material-slots-20261020/build-summary.json)。该包正式安装、17项实际哈希匹配；[488条冻结会话](../../docs/validation/runtime-material-slots-20261020/README.md)证明Canvas/BG Translucent槽Float创建/覆盖两次，manual与scene-change Dispose后租约失效、探针保留计数归零。TMP Color、动态128目标饥饿、基线恢复/像素/GPU仍未验收，不同包不能继承旧全部流程证据。

**603检查包**的[1095条独立会话](../../docs/validation/runtime-back-skip-20261020/README.md)验证Back→Aborted、延迟Skip→Skipped、scope先取消、新RunId，无伪造Result/重复/ObservationLost。[后续拒绝证据](../../docs/validation/runtime-back-skip-20261020/material-refusal.md)只证明旧目标/shader拒绝；前台F8动作由主代理报告、日志无回调，键盘动作仍未通过。

上一**582检查包**的[独立证据](../../docs/validation/runtime-cancellation-20261020/README.md)证明SDK暂停时钟冻结、继续稍后恢复、Retry Aborted及旧局审计/新RunId；当时Skip只有ObservationLost，失败证据保留，由603新会话独立补齐终止。更早按钮包的[音频/导航证据](../../docs/validation/runtime-unified-20261020/native-button/README.md)、SDK/模板、旧目录复现和性能工具只证明各自记录的包。离线通过或模块RuntimeProbed不等于全部能力实机验收，完整任务仍按SPEC原标准。

更早的 [双包实机证据](../../docs/validation/foundation-runtime-20261019/README.md)已证明插件存储迁移/隔离、语言热切、可选服务调用/撤回以及真实 444 谱面/444 成绩的分页修订一致性。每份证据只证明其记录的包；不存在“所有新服务均未安装”的结论，也不能据此验收全部新增模块。

## P0 当前缺陷与原生基础

**666后续949事件失败保留，源码修复不改写原包结果**：见[机器核对](../../docs/validation/runtime-visual-scope-cleanup-20261020/auto-return-proof.json)。前273清理成功事件保持不变；后续479仅为内层Hook计数，最后身份状态281观察/8未关联，689的ReadHoldNote元数据验证失败撤回本局身份；687成功auto.challenge-max样本不证明失败来源。返LevelList后仍AwaitingResult，无RunEnded/结果。929/931的VisualCleanupPending导致FrameUpdate失败、example.panel停用，模块恢复后插件仍Faulted。本轮已落盘视觉过渡和独立Hold协程上下文修复；结算来源隔离不能补造这次缺失结果。四项修复已通过691项整合并安装，仍待新包实机，B01/B09/B11/B12/B13均未验收。

- [ ] B01 单局事件闭环：Game77；分包证据已有失败、暂停恢复、Retry与603包Back/Skip；665三局不同ID、Failed/Aborted/Skipped，所选结算停留无重复结束。正常完成、初始化/未开局/停留结算/迟到结果完整矩阵仍缺，见[665证据](../../docs/validation/runtime-judgement-identity-20261020/auto-retry-skip-proof.json)。
- [ ] B02 原生类型/句柄/ABI：当前离线Runtime177；精确static/ctor/field setter及owned Arg线程保护、受控Scene句柄/字符串publication已接。846metadata、907参数/资源和953未激活controller的原生GC/后帧销毁分别有历史实机，953释放7root与观测根；1266受控激活/转场/设置及1287返回原语仍是离线实现与fixture。1298补真实Dobby/native Judge worker及SEH/C++活动证明；激活/发布后所有消费者结束、managed inner/Hold原异常、长期heap/GPU及完整矩阵仍缺，root计数不等于物理heap归零。

## P1 公共骨架

- [x] B03 SDK 契约（历史已验收范围）：原包 SDK 13 项及当时正式加载器/宿主运行证据保留，见 [历史说明](../../docs/SDK-compatibility.md)。该勾选只证明原包，不代表当前 API 或协议变更已重新验收。当前 B03 要求同一候选的 SDK、宿主、模板与独立插件合同一致，并验证错误范围、类型身份、接口和包格式的拒绝；不要求旧编译插件继续运行，每次变更按其实际新证据标注范围。
- [ ] B04 包生命周期：独立双包已离线验必需与可选依赖、版本、环、冲突、故障传播和跨宿主明确停用。当前候选实际安装替换与中断归属矩阵待验。
- [ ] B05 作用域/调度：Scope30；同session/run/scene/revision边界、迟到拒绝及提供者撤回；分包验证两owner scope先失效。666正式宿主父子视觉清理回归及两轮实机停用/中间启用通过、管理计数归零且Error0；逆序释放、队列/超时/迟到/重入/压力完整矩阵待验。
- [ ] B06 模块适配：精确清单、独立恢复/代次/依赖已实现，Module26；603实机22模块active及取消能力available，设置override正确等待；故障隔离、撤回恢复和旧服务失效矩阵待验。
- [x] B18a 结构化诊断：限流/轮转/上下文/单会话导出已接游戏，诊断 16、导出 9 及真实会话通过。见 [说明](../../docs/Diagnostics.md)。

## P2 共用接入机制

- [ ] B12 语义目标：Mutation21；跨registry代次隔离、4096窗口跨轮推进、满128仍刷新语义及安全轮换、50000历史边界已实现并整合717。当前离线Visual71/Runtime160；历史Visual68及Runtime57见[本轮证据](../../docs/validation/visual-admission-20261021/service-admission-checkpoint.md)。真实动态场景/重建/地址与实例ID复用矩阵仍缺，数组任意重排和大列表等待时长有明确边界。
- [ ] B08 修改协调：公共栈/变换/独占/基线/回滚及颜色接入已实现；新机制实际多包矩阵未验。
- [ ] B07 精确 Hook：当前离线Hooks189；18操作入口pre-init统一27写/V3、生产只读采用和双sink共享已接，4组新增合同、映像5074/6、CLR采用384/2及自有新桥837/5分别通过；真实游戏首发/metadata/callback仍缺。LOCAL五门、Progress/Timeline、typed Start/getter隔离及内部Boolean参数观察已有生产实现。1298接通Judge/Hold活动和物理entry查询，1315新增全线程标量probe及独立early-retained lane，旧Before→retain顺序保留；菜单新入口仍只有受控engine/静态验证。旧Judge真实Dobby worker/detach/外层SEH/C++、665历史675观察/原调用与8来源、native fixture31261/61358各自保留。完整LOCAL skip、所有在途撤回/重入/异常/优先级实机矩阵、新菜单及managed inner/Hold原异常和参数修改仍未全部验收。
- [ ] B09 生命周期：Game77；分包真实失败/暂停恢复/Retry及603的Back/Skip、scope先取消已有；665三局不同ID与Failed/Aborted/Skipped。本轮原调用结算来源保留与同谱迟到结果隔离已落盘，有独立红绿日志，已纳入717完整构建，新包实机待验。正常完成、初始化/中途启用/快速重复进入与真实迟到结果矩阵仍缺。

## P3 游戏公共服务

- [ ] B10 一致数据：Data 24，真实两包 444 谱面/444 成绩分页一致性已验；新当前局结果与设置源接入仍待完整回归/实机字段验证。
- [ ] B11 玩法：前包Gameplay30、审计17、Judgements27；公开逐音符服务、12固定调用点识别、真实Note/谱面图及作用域订阅已接通；665实机675观察/未关联0、8/12来源和17详细样本，自动头尾共享ID，第一局479计数与失败结果一致。本轮Hold尾部改用独立精确Boolean协程入口上下文，静态定位与回归已落盘，已纳入717完整构建，真实挑战尾部待验。665未覆盖的四来源仍须完整新包验证；666单个auto.challenge-max样本单列，不代替完整身份可靠性。手动Hold/长期GC/根释放、单位标定及完整结果/控制矩阵仍缺，Speed/Seek未开放。
- [ ] B13 视觉：当前离线Visual71、GameResources25；TMP_Text.color共享目标/仲裁和独立基线已实现，真实读回待验；恢复/回滚失败保护与延后销毁已落地；666前273事件的两轮正式停用证据保留。本轮修复发现过程中进入cleanup pending后的可用性重判，已有视图拒绝新写入且不误停插件，针对性回归通过，已纳入717完整构建，新包实机待验。native cleanup pending仍在，TMP Color、动态准入实机、物理释放/基线恢复receipt、像素/GPU、逐类双包及音符/轨道/相机/后处理仍缺。
- [ ] B14 UI/输入/导航：当前离线UI/input79；手柄直接current替换隔离与场景清理顺序已修；手柄16数字按钮、独立设备失效已接（尚未真实硬件验收）；已补采集/派发焦点与逐回调owner/epoch检查；644实体F8一次采集→Host排空→SDK回调，native键盘时间推进；分包原生按钮、overlay、文本和导航证据保留。手动更新/重复/重绑、双包焦点/模态、两个挂载点与停用恢复完整矩阵未完成，真实手柄及热拔矩阵未验，游戏输入截获未支持。
- [ ] B15 音频/本地内容：当前离线AudioContent640，返回ABI2/实际tracker身份关联已接，新增四typed engine节点观察/代次账本，尚无固定UnityPlayer安全发布/生产接线；六Stop源码观察/请求账本与共享Pending保护Ready和退休边界已接；22组新增受控回归及真实native→双账本五进程544检查通过，engine取消完成仍缺；新增10组实际生产根采用故障、8组mutation账本、6组协程身份受控回归；Stop请求已接线而engine取消完成未证，另有自有listener真实CLR/ledger联合12进程1630检查（图/根后端受控，非游戏/IL2CPP GC）；事务/LOCAL目录/revision/会话/结果claim及bundle、四池预留、实际setter、初始化保护、受控激活/一次Awake/六globals、真实首次转场与精确结果sink已有生产类内调用链及离线回归。814wrapper/846metadata/907参数与953未激活clone分别有历史实机；旧六null/false基线证据不等于新非空发布/恢复已实测。菜单输入/相机交接、active消费者及资源/coordinator退休、完整返回/新图重试和Loader准入仍缺；Workshop非空正向、可闻输出/混音/延迟及完整矩阵未验，RequestPlay仍OperationUnsupported。
- [ ] B16 存储/语言/设置。已有双包迁移、隔离和语言热切记录。r4 在 Steam 实测两个普通插件将背景音乐从 100% 逐层调至 98%，再逐层恢复到 100%。r17 冻结源码修复了四种音量滑块的微调被吞掉，以及停用插件时误覆盖外部微调的问题。设置 55/55、资源修改 21/21 通过，新包实机仍待完成。可写设置限于适配器已验证的字段。[设置回归](D:/riz-m2/b16-settings-adjacent-values-20261123/green-tests.log) · [实机记录](../CURRENT-CHECKPOINT.md)
  - r17 封包后，主树修复插件在存储回调中再次保存时，后续订阅者先收到较新版本的问题。存储与语言回归 21/21 通过，覆盖同插件嵌套保存、跨插件独立通知、停用和订阅者故障。[r17 复现](D:/riz-m2/b16-r17-basic-qualification-20261123/public-config.json) · [主树回归](D:/riz-m2/b16-postr17-storage-notification-order-20261123/green-tests.log)
- [ ] B17 跨插件服务。r17 Basic 组件的服务回归通过 26/26，覆盖饱和队列中的故障交付。用 r17 作者包编译的三个普通插件在六个独立进程中通过 17 项检查，覆盖跨 ALC 的类型与版本、软硬依赖、重复服务、故障归属、停用与恢复。仍需在 Steam 的 Unity 主线程上验证提供者故障和依赖插件的联动。[r17 跨插件回执](D:/riz-m2/b17-r17-plugin-host-20261123/run-03/validation.json)

## P4 发布与维护

- [ ] B18b 性能/压力：工作树有有界回调分位数/分配及资源采样，Performance 11；四种基线、20 轮真实切换、资源曲线与校准阈值待完成。默认仅观察。
- [ ] B19 分发：上一完整构建25套1617/1617、599源/23包；随后安装安全16/恢复13修复，现组合源再增至本批604并通过新核73/旧发布器57/旧链73。23包仍原字节，新链未安装。生产启动/V4、真实对象接线、调度/退休、游戏流程、干净环境与升级矩阵仍缺。见[当前证据](../../docs/validation/foundation-early-kernel-20261025/README.md)。

## 实现顺序与交付位置

这是任务依赖，不要求启用多个代理或创建其他聊天。一个实现者也可以按顺序完成。

| 任务 | 前置任务 | 主要改动位置 | 必须提交的证据 |
|---|---|---|---|
| B01 | 当前补丁已安装 | Adapter/GameEventObserver.cs、GameEventState.cs、Game.Tests | 五类真实流程的事件序列与关联结果；不伪造漏失事件 |
| B02 | 当前代码基线 | Runtime/NativeRuntime.cs、内部 Interop/Handles、原生边界测试 | ABI 支持/拒绝表、布局测试、实际调用与回收结果 |
| B03 | 当前候选 SDK 合同 | Sdk、当前合同测试、docs/SDK.md | 当前 SDK 编译插件经正式宿主运行；共享类型/结果一致及错误合同拒绝 |
| B04 | B03 | Hosting、Core/PluginHost.cs、配置与安装脚本 | 依赖/冲突/迁移/跨重启测试 |
| B05 | B03 | Core/PluginScope.cs、MainThreadDispatcher.cs、服务代理 | 取消、迟到任务、重入、子作用域清理 |
| B06 | B02、B03、B05 | Adapter/RizlineAdapter.cs、BuildProfiles.cs、Loader 组合 | 模块和符号探测报告、单模块失效恢复 |
| B12 | B05、B06 | Adapter/Targets、Sdk 目标契约 | 活跃/模板区分、歧义与对象重建 |
| B08 | B05、B06、B12 | Core/Mutation、Adapter/GameResourceService.cs | 多持有者交错启停、基线变化、独占冲突 |
| B07 | B02、B06、B08 | Runtime/Hooks、Core 扩展点、Adapter 绑定模块 | 一个真实方法、多订阅、原调用计数与拒绝路径 |
| B09 | B01、B05、B06、B07 | Adapter 的 Scene/Run 模块、Sdk 事件 | 初始化/迟到/重试/快速切换序列 |
| B10 | B05、B06、B09 | Adapter/GameDataService.cs、Sdk 数据服务 | 快照一致性、字段语义与现有样本回归 |
| B11 | B07、B08、B09 | Adapter 的 Timing/Judgement/Control 模块 | 精确判定计数、三种时间源、每项控制有效/拒绝路径 |
| B13 | B05、B08、B12 | Adapter/Visual、Runtime 对象桥、资源探针 | 每种已开放资源的真实双层覆盖和释放 |
| B14 | B08、B09、B12、B13 | Adapter/UiInput、Sdk UI/输入契约 | 双插件焦点/按键/挂载/导航和停用恢复 |
| B15 | B10、B11、B13 | Adapter/Audio、Content、SDK 提供者契约 | 真实音频与本地内容加载、失败和租约释放 |
| B16 | B04、B05、B08 | Core/PluginConfiguration.cs、Hosting 迁移、Adapter/Settings | 迁移、配额、语言、设置基线恢复 |
| B17 | B03、B04、B05、B06 | Core/Services、Hosting/Contracts、Sdk | 版本/类型身份、提供者消失和异常归属 |
| B18 | B03；完整测量在相关模块交付后 | Core/Diagnostics、Loader 状态输出、证据脚本 | 可查询原因、日志轮转、机器基线和资源曲线 |
| B19 | B01–B18 的发布范围均验收 | scripts、模板、docs、构建产物 | 同一候选可复现包、安装替换/中断恢复、错误组件组合拒绝、支持表与 SDK 文档 |

目录名是拟议位置；优先复用现有职责相符的文件，不能为了对应表格机械搬移所有代码。

## 每次交付的统一要求

每个任务附变更摘要、实际可用范围、自动检查、真实证据、已知限制、回滚方式。证据放在 `docs/validation/<任务ID>-<日期>/`，含构建指纹、包哈希、测试脚本/命令、所选会话日志及结论；不要覆盖旧失败证据。

当前功能验收至少覆盖：正常使用、输入/状态不符、两个插件共存、能力撤回、停用撤销、目标销毁/重建、当前合同一致和错误合同拒绝。历史版本兼容不列入当前矩阵；性能成本与新基准按用户安排后置。确实不适用的列说明理由，不能默认为通过。历史记录中的“升级矩阵”按当前包安装替换、中断恢复与归属保护理解，不要求旧 SDK/API/包/协议运行兼容。

## 第一个可直接交给实现者的任务

> 从 B01/B02 开始收口。先执行统一构建，记录实际安装包清单与所选新会话，读取控制器诊断，再用正常结束、失败、重试、暂停恢复、提前退出验证生命周期。若仍漏开局，定位实际活跃控制器/字段或精确开始方法，不能通过进入场景或事后结算合成开局。对确定原因写回归测试；未知范围继续列出。随后建立原生类型支持表和拒绝路径。不要增加业务 Mod，也不要把后续任务的空接口算成果。若没有新游玩证据，可以完成独立 ABI 和契约工作，但 B01 保持未验收。
