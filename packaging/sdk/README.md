# RizToolbox SDK 1.6

这是给 Rizline 插件作者使用的**仅编译期引用包**（Reference-only NuGet Package）。游戏运行时的实际实现由已安装的 RizToolbox 宿主提供，**不要**把任何 `RizToolbox.*` 宿主 DLL 打包进你的插件目录。

## 安装引用

```xml
<PackageReference Include="RizToolbox.Sdk" Version="1.6.0"
                  IncludeAssets="compile" PrivateAssets="all" />
```

## 基本开发规则

- **项目结构**：目标框架设为 `net6.0`，实现 `IRizPlugin`。把编译出的插件 DLL、插件自己的私有第三方 DLL 和对应的 `plugin.json` 放在游戏目录的 `BepInEx/RizToolbox/mods/<插件ID>/` 下。
- **生命周期**：入口类的公共无参构造函数不要做任何游戏或宿主操作；在 `Enable(IPluginContext)` 里获取服务和注册回调。
- **主线程与作用域**：在 Unity 主线程调用 `context.GetService<T>(capabilityId)`。后台线程只能持有不可变的数据快照；通过服务拿到的资源句柄和回调会随插件停用或子作用域结束自动失效，不要跨次启用继续复用。

## 版本与能力检查

- SDK 1.x 保持统一的程序集标识 `RizToolbox.Sdk, Version=1.0.0.0`。
- 宿主会按插件清单里的 `minSdk`（包含）和 `maxSdkExclusive`（不包含）检查当前合同版本 `1.6`。
- SDK 存在不代表每个游戏版本都开放了所有能力，具体能力和实验性操作都应在运行时先查询是否可用。

## 统一错误处理

所有操作结果（`OperationResult`）与服务查询失败都通过 `Error` 暴露结构化的 `OperationError(Code, Message, CapabilityId, TargetId, Retryable)`：
- 写分支逻辑请检查 `Code` 或 `KnownCode`，`Message` 只用来展示给用户。
- 构造结果使用 `Ok(...)` / `Fail(code, message)`（不再提供纯文本 `Fail` 重载），或直接转发已有 `OperationError` 以保留元数据。
- API 合同变动后请用对应的新版 SDK 重新编译插件。

本包只包含编译期引用程序集与 XML 注释文档，不含游戏二进制、运行时加载器或原生库。完整的插件模板和开发文档随宿主源码及作者包一起发布。
