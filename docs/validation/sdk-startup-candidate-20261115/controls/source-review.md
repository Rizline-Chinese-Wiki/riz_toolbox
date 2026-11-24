# complete1172 当前启动候选的 SDK14 范围

本轮复用原 `Test-SdkDistribution.ps1` 的 14 项正式宿主检查和两个模板插件探针，独立构建 complete1172 的正式宿主、SDK 引用包、AuthorOne 与 AuthorTwo；旧1171的运行结果不用于接受本轮。完整冻结源取自 `runtime-tick-admission-20261115/run-r2/source`，以原结果 SHA-256 与 supervisor34372 对 controller43772 的原始 `Popen.wait` 自然退出0为前置条件。

1172 包含 Distribution.Tests 的两个实际源码；它们是验证角色重叠，不额外计数。另冻结候选辅助 Build 脚本，以及本轮当前 Pack/Test 脚本、packaging SDK readme 和整个 riz-plugin 模板。主工作树所有源文件保持只读；构建产物只生成在新的 D盘范围内。正式宿主编译闭包是项目引用决定的 SDK/Core/Hosting；冻结 Runtime/Adapter 不构成生产 native 或真实游戏 provider 验证。

环境适配只改变离线依赖 feed、新输出目录、NuGet 审计/构建编排和原控制器监督；原 Pack 脚本不修改，原14检查与嵌入探针不修改。四个 net6.0.36 nupkg 固定为之前本机缓存原字节；正式宿主、插件恢复使用本轮新 cache，模板只装入本轮 custom hive。子环境只覆盖五个常用 dotnet 首次运行开关，后台 Python/CLI 启动均显式 `CREATE_NO_WINDOW`。

本轮仅独立 SDK14 候选证据，不宣布完整 SDK、B01–B19、engine/source/FullSeek 或实际游戏 Speed/Seek 完成；候选未合主、未部署、未启动游戏。保留原受监督等待凭据，launcher0不作为controller0。根代理另行只读复核。
