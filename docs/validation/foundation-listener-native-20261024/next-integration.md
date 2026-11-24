# 下一阶段：发布身份与实际CLR listener

前提：本批1515/1515、25套、491源/22包冻结未安装，自有原生listener 8349项。本批listener为native only，不是CLR listener/managed ledger接入；没有固定游戏六区发布，不升级FirstInstall V1 Ready144。invokable dispatch不等于每个delegate leaf；真实游戏receipt callbacks、冷启动、IL2CPP/GC、连续mutation/provider转交/取消、全部future来源与消费者退休、完整Retry/Return/Loader仍缺。 本文件为计划，用户“实测稍后”有效，不能当作验收证据。

## 1. 收口原生fixture剩余分支

当前明确未实现/未验：dynamic-vtable-mutation、target-read-av、foreign-parent-mask-and-overflow、missing-post-corruption、full-exception-record、prehelper-every-instruction-unwind、all-delegate-leaves、mutation-stopcoroutine-retirement。另外还需取得错post list/index与独立lane父依赖Unknown时序的确定注入证据。按真实机器路径增加故障用例，不把人工构造frame包装成真实callee执行；overflow与foreign必须遮蔽祖先，missingPost只能Abnormal并保Unknown，不能补造normal。现有pre真实callee与post外侧叶快照的GPR/XMM/control核对继续回归，不扩大AVX/x87 data保证。

virtual一次目标读取位于observer保护之外，读取异常的地址是helper真实指令，不能伪称原CALL RIP。TargetReadFaulted的Abnormal After(token0)可能没有Before，未来managed只能按flags做native cleanup，不能从0推已capture。该分支没有本批动态验收，不冒充原callee SEH/C++用例。 两份独立descriptor不是聚合V2；若下一版需要整份callback原子快照，应版本化ABI并给明确迁移/旧调用隔离，而不是热补已Attach表。

## 2. 版本化生产publisher与receipt

固定profile先核三处source ReturnPC、6/9/9完整continuation、原primary unwind/frameRegister，再准备近端stub、完整CHAININFO和永久root。全部table注册、真实lookup与边界展开证明必须在首写前完成；有写尝试后Unknown不得释放。新receipt明确六区、各stub/target/epoch/页保护/原bytes/写数和publisher身份；旧V1 Ready144三写不得授权六区。

先在只映射固定GameAssembly的隔离进程核真实bytes与页权限，绝不调用游戏函数。自有fixture代码写入不是游戏生产发布，也不能以known代码猜测任意RVA安全。

## 3. managed adopter、ledger与全run闭环

真实CLR listener采用应核新receipt与原生full identity，共享完整slot/epoch/site/PC/target；保持生产RequireReady前门，不公开SDK bypass。Before/Returned/Abnormal按private parent/child sequence与受控token关联同代context；TargetReadFaulted没有Before时只nativecleanup，token0不能证明capture。机器帧只读copy且不可跨callback持有。

继续实际IL2CPP invokable/provider/对象根、动态listener leaf与异步转交、连续mutation和取消；invokable dispatch不等于全部delegate leaves。来源未闭合保持opaque/Pending。全部future及消费者结束后才组合VerifyConsumersEnded→Close→native/图/audio/globals/menu/result/settings资源退休，最后完整Retry/Return/Loader及真实冷启动/game acceptance。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。
