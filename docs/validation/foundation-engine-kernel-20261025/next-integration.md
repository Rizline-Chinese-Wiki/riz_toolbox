# 下一步接入固定早期链，再实现生产 bootstrap

本批交付的运行核已经具备独立ABI、永久槽和实际调用证据；目前只作为新的测试与包组件。它没有自动取代生产Runtime所采用的旧helper，也不能与旧模块共享槽、epoch或Ready。

1. **重用固定发布实现建立独立Kernel绑定。** 当前 `FixedPublisher::Bind` 明确只接受 JudgeContext，旧73用例必须保留其原身份。下一批用受编译期profile约束的独立publisher变体绑定精确 `RizToolbox.EngineNodeKernel.dll`，复用发布/原函数桩逻辑，区分模块名、静态导入和receipt身份。继续只允许自有Targets/marker，不顺手泛化为任意地址API；现有固定组件行为必须回归。
2. **把新的小组件链实际连通。** Proxy/Recorder使用普通静态导入的新Kernel+新publisher，保留noCRT构建；真实Importer TLS/C/C++/DllMain四入口、加载后Attach、worker、Start嵌套、完整EAX/前缀副作用和未发布异常基线都须实际执行。重做56内部故障与Busy永久拒绝、每次实际字节/共享页保护；debugger补充证明新核无entry/TLS且代理初始化先于Targets。系统OS内部依赖据实际必要调用评审，不能无限扩大成逐个系统DLL穷举才能编码。
3. **实现独立生产入口与事实凭据。** 采用[已提取的四个WinHTTP按名导入](../foundation-winhttp-contract-20261025/research/game-winhttp-contract.md)，保留系统模块获取/转发执行的独立验证。新的生产bootstrap固定UnityPlayer/GameAssembly/Kernel身份、RVA/完整前缀与unwind，并证明首次相关执行之前的可信时机。pre-il2cpp_init不能抹除UnityPlayer此前初始化；自有marker、测试receipt或改模块名均不授游戏权限。
4. **凭真实producer完成CLR只读采用。** 先有实际单次发布和独立V4，再核查NativeEngineNodeContextBridge采用已发布target/original/entry/epoch，接coordinator、真实root/owner/Enumerator/Coroutine代次与GC；调用前后责任、scheduler/async/Start内联清理、所有消费者排空分别完成。最后才能形成取消完成与资源退休凭据。
5. **补完整验收。** 游戏实测恢复后做冷启、暂停、正常/失败结果、重试、返回菜单再开局、Loader重建，以及双插件共存/失效/对象重建/性能。统一构建、分发、旧安装归属与回滚证据一并更新，按完整[B01–B19](../../../.docs/foundation-expansion/SPEC.md)验收。

这些是继续实施的具体依赖，不能把新核的7个进程或25套离线通过换算为整个底层完成。当前未安装；用户要求的“先继续代码，实测稍后”保持生效。
