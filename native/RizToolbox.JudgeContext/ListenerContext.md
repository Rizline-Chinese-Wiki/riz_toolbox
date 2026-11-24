# Listener 原生观察通道 V1

2026-10-24。实现源码已冻结，尚未构建或运行本批 fixture。本文件记录内部 ABI 与实现边界，不是游戏接入或六区发布通过证明，不增加 SDK 任意 native hook 能力。

## ABI 与配置责任

本通道按既有 Bootstrap slot 使用，共32个永久槽。新增五个导出：RizListenerContext_GetPreEntry、GetPostEntry、Configure、Attach、GetInfo，helper 导出总数由原36增至41。ASM entry、dispatch 与父记录访问函数均为内部符号，不是额外导出。

| 结构 | 大小 | 内容 |
|---|---:|---|
| ConfigV1 | 80 | Version/Size、实际Bootstrap parentEpoch、三个sourceReturnPc、三个postTransportReturnPc、两个directTarget |
| ObserverV1 | 32 | Version/Size、Before、After、User；两callback必填 |
| FrameV1 | 176 | slot/site/stage/thread/flags、lane epoch、private parent/child sequence、parent managed token、event/list/index、invokable/四参数/actual target、分开的source/transport return PC、source RSP与只读machine frame |
| InfoV1 | 176 | slot/attached、lane/parent epoch、pre/post entry、完整PC/target配置、entered/exited/inFlight/faults/abnormal/overflow/missingPost、Unknown |

精确字段偏移由 [ListenerContext.h](ListenerContext.h) 的 static_assert 固定。site为0/1/2；stage为Before=1、Returned=2、Abnormal=3。flags：BeforeFaulted=1、ParentAbnormal=2、MissingPost=4、TargetReadFaulted=8。managed child token为零合法，不能据此推断没有Before。

调用方先取得真实Bootstrap parent epoch，再Configure listener，读取独立lane epoch后Attach一份不可变observer。配置与observer复制进永久状态；delegate、User、helper、stub和相关native原函数地址必须存活至进程结束。没有卸载、替换、Detach、重置或槽复用。

Configure只建立配置，不写机器码。0 ready、1 invalid、2 slot claimed、3 PC collision、4 parent not ready、5 parent epoch mismatch、6 unknown；六个PC在本配置内及跨槽均不可重复。Attach返回0 attached、1 invalid、2 not ready、3 lane epoch mismatch、4 attach attempted、5 unknown。GetInfo返回0 ready、1 invalid、2 not ready，即使不ready也报告真实sticky Unknown。失败的永久claim不可重试。

## 父快照与子调用

本版采用独立listener lane，保持原Bootstrap V1 ABI和三个原导出不变。每个Bootstrap私有TLS record在读取自己的V1 descriptor后、任何managed Enter前，固定一次listener descriptor。V1 parent observer可以为空而listener observer非空；此时ParentManagedToken=0合法。它不等于把所有callback合成一份新V2 observer。

listener Pre只接受当前Bootstrap TLS顶层处于original window、slot/parentEpoch/self与捕获相符的记录；parent Enter fault、foreign slot、overflow或未配置父不会授予外层权限。父record与child record地址会复用，所以private parentSequence/childSequence为进程单调序号，不能用地址代替代次。

每个已识别Pre都建立child或overflow mask，即使父listener snapshot为空。Post不重新读published observer，仅结束自己的child快照。旧父Enter后才Attach不会改变该父descriptor。Post必须匹配实际transport PC、slot/site/source RSP/event/list/index和当前parent/sequence；错配令Unknown，继续原native continuation，child留给父finally发Abnormal，不伪发正常After。

child TLS上限256；overflow以固定mask与每槽计数遮蔽祖先，每个父record保存baseline。异常没有Post时，父finally先清自己的children/masks，再执行原V1 parent Exit。原callee SEH/C++产生ParentAbnormal，不因原异常本身自动令lane Unknown。正常父返回却留有child则MissingPost、Unknown并发Abnormal，不补造Returned。

Before/After observer的native SEH/C++被包含，置lane与Bootstrap Unknown。Before fault的managed token为0，后续正常Post仍向同descriptor发Returned|BeforeFaulted，以便清理；After fault仍须pop。Unknown禁止新Attach/复用，不移除既有清理callback。无法证明配对时保留Unknown责任，不把计数伪装成Known drain。

## 机器现场与真实返回地址

Pre是真实CALL入口。保存GPR、XMM低128位、RFLAGS、MXCSR、x87 control word，callback后恢复。两个direct target按配置原地址跳转；virtual只在native内按保存的RAX读取[RAX+178h]一次。最终target暂存在原callee的home[0]，也就是入口I=RSP的[I+8]，合法epilog后进入独立无栈leaf jmp [rsp+8]。[I]的真实source return PC不改变。

home[0]是允许callee使用的shadow scratch，不能作为透明不变canary；不碰[I]、第五参数[I+28h]或parent非易失保存区。这里没有保存AVX upper lanes、完整x87数据栈或全部浮点状态的承诺。callback必须把frame/machineFrame视为只读且仅在调用期间有效，不能留存指针。

virtual target实际读取在observer SEH保护之外。读前已建立native child；若读取AV，异常穿过Pre真实unwind到父finally，发Abnormal|ParentAbnormal|TargetReadFaulted、managed child token=0。**此时Before尚未调用，也仍会收到Abnormal After用于结束native记录；不能假设每个After都有一次Before。异常地址是helper实际读取指令，不能声称仍是原virtual CALL的RIP。** 不把它当observer故障吞掉，也不改写CONTEXT冒充原现场。未知Pre PC无可恢复target，直接failfast，不跳零、不跳过原函数。

Post helper同样由真实CALL进入，返回到post stub的真实transport PC；machineFrame保存callee返回后的现场，source PC独立保留。Post不调用原listener、不重放continuation。近端零栈stub须负责完整inc ebx/mov eax,ebx/jmp loop replay与动态CHAININFO注册，这由单独publisher/fixture负责，不能把JUMP到达的原parent frame当作普通CALL frame。

## Unknown、统计与待接入

listener Fault单向通知所属Bootstrap state，不形成相互GetInfo调用。管理Configure/Attach/GetInfo刷新Bootstrap的Void/Gate/Mid底层故障，再读真实Unknown；CurrentParent与既有清理路径不因Unknown丢掉责任。没有配置listener的V1路径保持旧ABI与观察语义。

Info inFlight仅覆盖Pre到Post/父异常清理。ASM之后仍有restore/tail/replay/parent Exit，完整物理drain必须继续依赖既有Void admission，不能凭listener inFlight=0授予改写或释放权限。

本轮没有游戏机器码publisher、实际游戏六区receipt、生产managed adopter/ledger listener关联，也没有SDK listener接口。旧FirstInstall V1 Ready144三写只代表旧runtime/parent/Mid，**不授权新增六区**。生产接入还需要版本化receipt、固定映像profile、首写前全部stub/function table验证、写后Unknown责任、真实IL2CPP/GC/owner/未来来源与取消、全消费者退休、Retry/Return/Loader及冷启动验收。

下一步由root构建自有fixture，验证实际callee _ReturnAddress、两direct加一virtual参数/flags/原调用一次、post replay、动态链真实RtlVirtualUnwind、正常/SEH/C++/回调故障、递归/worker/旧调用跨Attach与有界overflow。本文没有把这些尚未运行项目写为通过；RequestPlay与B01–B19验收状态保持原样。
