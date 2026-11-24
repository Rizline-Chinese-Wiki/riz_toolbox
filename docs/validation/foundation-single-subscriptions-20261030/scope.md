# Single 订阅与同步清理范围

日期：2026-10-30。授权：用户继续 PC Mod B01–B19 底层实现，root 明确委派本包；功能优先，性能后置，无旧 API 兼容要求。network_profile=offline。没有新建代理。

in_scope：Runtime/Hooks 的 NativeTweenSingleEventBridge、Subscriptions、Scope、PhysicalParent；Adapter/AudioContent 的 NativeLocalTweenBackend.SingleDispatch、TypedEvents、WorkOwner.TypedEvents/TypedEffects/TypedDispatch；NativeLocalTweenBackend.Retirement 仅 WithdrawTweenObservationBindings 与 VerifyTweenCompletionBindingsWithdrawn 两函数；tests/RizToolbox.SingleEvent.Tests；本验证目录。

out_of_scope：native 源码、pack、root-owned NativeFirstInstall、其它 B01–B19 并发模块、全树统一验收、真实游戏启动、GUI、真实安装、提交/推送、性能研究。

依赖基底：D:/riz-build-cache-next/foundation-single-v7-core-r1-20261029/source-snapshot，622 份编译输入。仅复制 .cs/.csproj/.json 与顶层构建配置，不复制 obj/bin；本包覆盖后 626 份输入。Retirement 的非 owned 函数仍取自固定基底。固定现成 helper：D:/riz-build-cache-next/single-v7-native-a-r3-20261029/RizToolbox.JudgeContext.dll，SHA256 30ec8ed7245432ff81490cc346fb13d03dca060bb0ef9575f90e2a3078779bde。

测试允许在独立 CLR 进程配置该 helper 尚未使用的 Single1/slot31，original 为永久保留的受控 CLR 委托，调用真实 native entry 和生产 managed broker。没有伪造 FirstInstall Ready、生产 publication 或 Bridge.Delivery，也没有游戏模块。private scalar parent association core 的测试通过 reflection 观察，不能据此声明 Single native producer ReadCurrent 的正向回调通过。效果结束和根释放没有新增完成凭据。
