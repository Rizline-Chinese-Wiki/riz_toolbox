# 跨插件服务撤回与独立包依赖

调用结果绑定到原服务登记和消费者作用域。消费者停用时，在途响应会丢弃，异步等待会取消。登记撤回也会结束该登记的异步等待。提供者开始停用时即从 Lookup 消失。 [服务登记与代理](../../../src/RizToolbox.Core/Services/InterPluginServiceRegistry.cs)

主线程迟到的故障处理会核对原登记。撤回通知若同步停用并重启了同一个插件，故障回调也会核对原作用域。真实提供者异常仍走宿主停用流程。改前复现六处失败，其中两处会把旧实例的异常送给新实例。改后 Services 25/25、Core 26/26、Hosting 24/24、PackageLifecycle 15/15。 [改前日志](../../../work/b17-service-lifetime-20261121/before-tests.log) · [定向回归](../../../work/b17-service-lifetime-20261121/current-r1/validation.json)

从当前 SDK 引用包编译独立提供者与消费者，分别验证了可选、必需、版本不匹配、环和冲突。每组使用正式包加载器和两个独立的 AssemblyLoadContext。九项检查另外覆盖依赖拓扑停用、保存的启用意愿跨宿主重建、跨包消息隔离、服务递归和同名配置交错迁移。首次构建缺少 .NET 6 引用包，后续从本机 NuGet 缓存补齐并重新生成两包。 [独立包结果](D:/riz-m2/b17-independent-20261121-r2/result.json)

标准模板另外通过 14 项，包括跨包错误字段、私有接口身份拒绝、错误线程和失效范围。两组独立包验证使用相同的当前 Core 输入及 SDK 引用包。合计 113 项。修复已进入源码，下一候选需纳入当前 Core。真实游戏中的加载、设置恢复和安装替换仍待验证。 [标准模板日志](../../../work/b17-service-lifetime-20261121/standard-template.log) · [汇总](../../../work/b17-service-lifetime-20261121/validation.json)
