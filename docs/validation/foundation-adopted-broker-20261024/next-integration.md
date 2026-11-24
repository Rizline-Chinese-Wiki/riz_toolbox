# 下一阶段：真实listener与固定游戏启动

前提：本批1515/1515、25套、484源/22包冻结未安装，额外379项自有fixture回调通过。共用生产配置通道采用与dispatch core已执行真实callbacks，原函数是自有fixture。它没有生产FirstInstall receipt，没有运行GameAssembly，不解析受控高位MethodInfo/Class指针，也没有真实ledger capture。不能称为固定游戏receipt已触发回调。 本文是计划，用户“实测稍后”保持，不能作为已运行证据。

## 1. 实施三处listener的六区运输

按[native-listener-design](native-listener-design.md)，两direct CALL保source+5，virtual六字节用90 E8保source+6；pre完整保存后尾跳真实callee，post E9进入动态stub、真实CALL观察helper并完整replay原continuation。post没有原callee的新CALL栈顶，必须以原frame chained unwind与真实RtlVirtualUnwind逐边界实证；禁止普通C++再CALL伪装透明。

在自有原生函数先核真实_ReturnAddress、所有原参数/flags、原调用与replay一次、normal/SEH/C++、递归/worker、旧调用跨Attach、溢出遮蔽、子代token和父finally清理。V2 observer一次冻结及V2 receipt明确六区责任；旧Ready144三写不能授权新六区。完成后再以固定映像隔离fixture核bytes/页保护/function table，不执行游戏。

## 2. 固定游戏receipt到真实回调与冷启动

生产RequireReady/UnityTarget/Gate复核继续保留；Info56不含expectedMidReturnPc：adoption可读回slot/target/original/entry/epoch；mid来自发布者tuple、共享字典严格比对，实际回调再核ReturnPc。生产post-Verify失败撤本次binding/probe且置Unknown，只完成源码复核，未进行该生产分支的动态故障注入。 先补可审的实际metadata/父调用/receipt联合证据，再在获准游戏实测时验证Doorstop→producer三写Ready→原init一次与真实int/原异常→CoreCLR采用→首合法tick/UnityEvent的同会话顺序。本批自有callback及上一批双idle sink采用均不能代替这条固定游戏执行链。

## 3. listener/future及全部消费者退休

将真实executing-list的三条调用关联当前parent/child与同代ledger，闭合动态leaf/provider、异步转交、连续mutation与取消；未证明来源保持opaque/Pending。全部未来来源结束后，组合audio/图roots/globals/menu/result/settings逐owner证据，才执行VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，最后同revision重做Retry/Return/Loader。

每种normal/失败/取消/provider撤回与多轮重开核原调用一次、Unknown不释放、条件式基线恢复、资源计数不单调增长。完整B01–B19标准不缩小，RequestPlay关闭，仅B03/B18a勾选。旧1021/18/326与原游戏文件保持原证据边界。
