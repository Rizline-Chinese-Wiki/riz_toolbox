---
plan: compatibility-foundation
version: 1.0
created: 2026-10-19
status: active
---

# Architecture: compatibility-foundation

## Structure

```text
.docs/compatibility-foundation/     任务、规格与验收状态
src/RizToolbox.Sdk/                 插件契约、业务 DTO 和扩展点契约
src/RizToolbox.Core/                生命周期、能力、作用域、扩展点和配置
src/RizToolbox.Hosting/             插件包发现、隔离加载及诊断
src/RizToolbox.Runtime/             IL2CPP API、原生 Hook 与通用 Unity 桥
src/RizToolbox.Adapter/             Rizline 构建配置、游戏事件及业务服务
src/RizToolbox.Loader/              Doorstop 入口与以上组件的组合
examples/                          可独立分发、仅依赖 SDK 的插件包
tests/                             核心、包隔离、适配输入与安装验证
scripts/                           构建、安装和卸载
docs/                              使用说明、SDK 与运行证据
```

## Dependency direction

Sdk 不引用其他项目。Core 引用 Sdk。Hosting 引用 Core/Sdk；Runtime 不引用具体游戏与示例；Adapter 引用 Runtime/Core/Sdk；Loader 组合以上层，完全移除对 examples 的编译引用。Mods 仅引用 Sdk，其私有库按包隔离。

## Execution model

Doorstop 进入 Loader，选择构建配置，Runtime 建立单一桥接入口；Unity 主线程创建宿主和 Adapter。Adapter 探测符号、注册能力及扩展点。Hosting 加载独立插件包，Core 根据依赖、能力和启用意愿进行 reconcile。每帧发布 tick 和观察到的真实游戏事件；生命周期、回调及资源操作都在主线程，磁盘 IO 使用快照或后台处理。配置文件和每包依赖目录由宿主提供。

## Ownership and compatibility

每次启用创建新作用域，服务视图带插件身份与激活状态，自动登记资源；停用先取消回调再逆序清理。插件级作用域是生命周期边界，AssemblyLoadContext 是依赖解析边界。SDK 共享类型身份，破坏性修改升级契约版本并准确标注迁移。Adapter 的构建配置是版本支持边界；无法验证的新构建不得启用写操作。

## Diagnostics

日志、status.json 和命令机制保留，增加每包加载错误、插件等待/故障原因、能力/扩展点清单和构建配置标识。所有本地 API 错误返回结果或清晰异常，不使用网络协议错误码。开发自测显式开启，日常不自动退出游戏。
