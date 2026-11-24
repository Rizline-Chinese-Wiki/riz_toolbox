# 范围与授权

- auth：用户授权继续实现 Rizline PC mod 兼容底层。父任务明确授权本子任务添加只读ResourceReference元数据诊断，并分别批准独立Runtime partial、既有公开IL2CPP API和lazy可选 `il2cpp_object_get_virtual_method`；禁止构造native wrapper/Unity资源、Invoke、hook或猜测Il2CppClass布局。
- in_scope：`src/RizToolbox.Runtime/NativeRuntime.MetadataDiagnostics.cs`、`src/RizToolbox.Adapter/AudioContent/LocalResourceReferenceDiagnostics.cs`、本目录报告。父任务负责共享模块接线、构建和实机。
- out_of_scope：修改主NativeRuntime.cs、SDK、UiInput、游戏文件；运行dotnet、安装、启动游戏、扫描live对象、修改gameplay和全量dump。
- network_profile：offline；本子任务无网络请求和外部消息。
- target：Steam Rizline Windows x64，Unity 2022.3.62f2；GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。
- source：前序dump.cs SHA256 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`，本阶段只读取所需字段/签名片段，没有重新反汇编二进制。
- current_result：代码和说明已落盘；无本子任务运行时证据，原生本地音乐/封面消费与slot映射均不据此标记完成。
