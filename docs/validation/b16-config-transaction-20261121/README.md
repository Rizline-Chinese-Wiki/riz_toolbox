# 配置保存、迁移和作用域停用

`IPluginConfiguration` 在默认值工厂、反序列化或序列化执行后重新核对作用域。旧激活的序列化回调若停用插件，写入返回 `ScopeExpired`，新激活已保存的值仍可读取。 [普通配置实现](../../../src/RizToolbox.Core/PluginConfiguration.cs) · [回归用例](../../../tests/RizToolbox.Core.Tests/Program.cs)

`IPluginStorageService` 只序列化一次，按即将落盘的实际字节数检查配额。迁移计入随后保留的原文件备份。同一文档的嵌套操作返回 `Conflict`，其他文档仍可处理。在替换前成功写完备份，替换失败时清理本次新建的备份和临时文件。停用发生在验证或序列化回调中时，原字节和修订保留。 [事务实现](../../../src/RizToolbox.Core/AtomicSchemaStore.cs) · [作用域和配额](../../../src/RizToolbox.Core/Storage/PluginStorageManager.cs)

七个问题在改前复现。改后 Core 26/26、Storage 19/19、PackageLifecycle 15/15、Settings 48/48，共 108 项。原子替换失败路径在 Windows 持有原文件的实际读句柄，PackageLifecycle 复验多线程保存。构建前后 1814 项输入一致。全解决方案 Release 为 0 错误、65 项警告。 [改前 Core](../../../work/b16-config-transaction-20261121/before-r1-Core.log) · [改前 Storage](../../../work/b16-config-transaction-20261121/before-r1-Storage.log) · [改后封存](../../../work/b16-config-transaction-20261121/current-r1/validation.json) · [解决方案构建](../../../work/b16-config-transaction-20261121/current-r1/build-solution.log)

新 V5 候选重建六个托管组件，冻结源码检查 1053 项及两组 V7 映像夹具 554 项通过。ZIP 58 个成员逐一核对，隔离 `ValidateOnly` 验证 26 个运行文件。从 ZIP 抽取 DLL 复跑上面四套共 108 项，配置和设置的独立消费者使用同源 SDK 引用包编译成功。 [归档核验](../../../work/b16-config-transaction-20261121/candidate-validation-r1.json) · [包内检查](../../../work/b16-config-transaction-20261121/packaged-b16-validation-r1.json)

双独立插件包共同运行的配置交错和真实设置恢复仍待验收。本批未安装或启动游戏。
