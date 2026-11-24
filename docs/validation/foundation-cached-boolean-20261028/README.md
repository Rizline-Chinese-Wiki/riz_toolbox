# 实际 Boolean cached listener 与当前 SDK 清理

2026-10-27 开始、2026-10-28 验证结束。已把实际 `CachedInvokableCall<System.Boolean>` 的官方元数据读取接入生产 Adapter 的完整监听器快照，并移除本轮不再需要的 SDK 转换与旧版兼容测试。没有安装、启动或操作真实游戏；生产首次执行准入仍关闭。

## 实际改动

[Runtime 只读解析](../../../src/RizToolbox.Runtime/NativeRuntime.CachedBooleanInvokable.cs) 从已有根取得实际闭类，核对 `cached<bool> → invokable<bool> → BaseInvokableCall`、官方 image 和 class/type roundtrip。通过官方字段枚举验证 `m_Arg1`、`Delegate` 的所属类、实例存储、类型与无 shadow，使用 `field_get_value` 读取一字节 Boolean；不使用 dump 中泛型字段的零偏移。

`UnityAction<bool>.Invoke` 只作官方签名证据，不解析其共享入口、不调用它，也不安装泛型 hook。固定 GameAssembly 的五个所需官方 export 已从实际 EAT 确认存在：class_get_image `0x283a30`、field_get_parent `0x283b90`、class_is_generic `0x283c80`、class_is_inflated `0x283c90`、object_get_size `0x2844d0`。

[Adapter 接线](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.CachedBoolean.cs) 按已核 FieldInfo 读取真实 Delegate，经 publication 回调先保留独立根，再检查目标、具体叶子 MethodInfo 与确切 Boolean 参数类。整个捕获结束后重读实际闭类、字段、参数、委托链和事件图；任一变化不能以先前描述性记录覆盖。publisher 失败沿既有调用责任保留，后续校验失败也不自行释放已采用的根。

固定 bool 参数及其实际来源进入 immutable invokable、Freeze、ListEqual、GraphEqual 和两处 mutation 比较。即使同一 list/version、同一 invokable 与同一 delegate 的参数从 false 改成 true，也能被识别。持久化候选仍是独立完整行，不按名字或列表位置猜配。普通非 cached bool、其它未知泛型、未支持签名继续 Opaque。

解析成功只补全调用内容；非空监听器的实际效果仍需独立 owner。[真实 CutOut 与 Tween 来源](../foundation-cutout-listeners-20261027/README.md)说明后续队列、发行关闭及 UI 后果责任。

SDK 删除 `ResultAdapters`、`SdkCompatibility`、`CapabilityInfo.ToLegacy`；Loader 直接从各服务自身的结果取得日志状态。SDK 测试移除旧二进制/API baseline 和旧插件行为矩阵，保留当前 Result、metadata、真实 PluginHost scope 与清单拒绝契约。Listener shape 入口统一最新 V4；保留当前记录必需的内部 416 字节结构。没有增加向后兼容分支或性能优化。

## 实际验证

| 证据 | 结果 | 范围 |
|---|---|---|
| [组合源码快照](D:/riz-build-cache-next/cached-boolean-core-r1-20261027/test-result.json) | Audio/content 866/866、协调器 28/28、对象身份 10/10 | 真实账本/桥与受控观察；新增 9 项 Adapter Boolean 场景。Runtime 编译 0 警告，Audio 夹具 18 条既有警告，均 0 错误 |
| [当前六套专项](D:/riz-build-cache-next/cached-boolean-managed-r2-20261028/test-result.json) | 468/468 | Adapter 10、Hosting 20、Runtime 216、SDK 7、Module 26、Hooks 189；包含 9 项官方元数据 projection/publication 语义场景。不是完整 24 套重跑 |
| [同进程 CLR 采用](D:/riz-build-cache-next/cached-boolean-adoption-r1-20261028/fixture-result.json) | 2 个独立进程 / 423 检查 | 当前源码采用实际 20 操作 producer，正常与重入拒绝；映像不执行游戏函数，IL2CPP metadata 是受控载体 |
| [六组件正式构建](D:/riz-build-cache-next/cached-boolean-release-r1-20261028/managed-build-manifest.json) | 实际 exit0、6 DLL、来源哈希不变 | 当前 Sdk/Core/Runtime/Adapter/Hosting/Loader；0 错误、1 条既有生成文件 nullable 警告 |

上述 Runtime 元数据测试通过受控 official API 绑定实际调用生产 projection，未在真实游戏对象上执行；不能把存在 export 与受控测试拼成一次真实 IL2CPP 运行成功。六套专项首次命令在分配输出目录前因数组被外层 `-File` 串成一个参数而拒绝，显式传入 PowerShell 数组后的 r2 才实际运行并通过。

对应结果 SHA256：

- core：`ec6caa7fd2ea3498a4d1debea39c00b5d37889701fa9ffb6dd0a7b00bda8dc66`
- 六套专项：`7ab7b793c8e83ed52eee3e7bc5d65016cdbf73ac08b36fb90669f24b3901fb8b`
- CLR 采用：`01a0594a4e55865449e1e4e439716dcc9cda93de8e5900a165b105ddaf74e9f7`
- 六组件构建：`f80cc5fd2acc75797e0ffbf213e4a248d4a38de5fd3cfc54f03eece7a0fc7146`

原生 helper 与 20 操作 / 29 写入合同没有变化，复用已验证的 native-r2、profile 与映像原件；本轮不重复与改动无关的原生机器/性能/旧协议矩阵。完整安装器 17 项验证属于上一份冻结的 20 操作候选，详见[该报告](../foundation-delegate-writer-20261027/README.md)，不能代称新载荷已跑同一矩阵。

## 本批候选

[foundation-current-candidate-20261028](../../../artifacts/foundation-current-candidate-20261028/transport-candidate-package-manifest.json) 实际打包终态 exit0（session48694），24 载荷 / 20 证据；manifest SHA256 `36a76b474e07c0be1ef11f8c46a38d4c4617842e227c2e8886530b105bddfbf0`。新六组件与 CLR 采用证明绑定同一 core 快照，固定 native helper、源码归档、profile 与映像证明沿用同一已验证构建；独立 PE/归档/哈希校验通过。候选仍为 `productionAdmissionGranted=false`、`executionWindowProved=false`、`gameExecuted=false`、`installed=false`。
