---
plan: runtime-redesign
version: 1.0
created: 2026-11-17
status: active
---

# Architecture: runtime-redesign

## Directory Structure

保留现有六个项目，不按目录数量判断轻重。以下路径已存在；括号内是待实现职责，而不是当前已具备的保证。

```text
riz_toolbox/
├── src/
│   ├── RizToolbox.Sdk/          共享契约、观察与显式命令
│   ├── RizToolbox.Core/         插件依赖、scope、需求计划和仲裁
│   ├── RizToolbox.Hosting/      清单发现、私有依赖、包加载
│   ├── RizToolbox.Runtime/      原生桥、安装验证、窄的运行操作接口
│   ├── RizToolbox.Adapter/      Rizline 对象绑定、模块、播放会话
│   └── RizToolbox.Loader/       模式、装配、host tick 和具名阶段
├── native/                     必须提前安装的入口和真实责任账本
├── examples/                   Author/Validation 内容，普通包不自动携带
├── tools/                      游戏外资源处理工具
├── docs/2026-11-17_runtime-redesign-report.md
├── work/runtime-redesign-20261117/   只读审查输入清单与核验结果
└── .docs/runtime-redesign/
    ├── PLAN.md
    ├── SPEC.md
    ├── ARCHITECTURE.md
    ├── CHECKLIST.md
    └── README.md
```

未来实现优先在所属项目新增独立 partial/内部类，不先搬移全部源码。新抽象只在确实减少职责交叉时引入；不新增第二套插件管理器、覆盖栈或脚本 VM。

## System Overview

```text
启动配置 + 包清单/启用意愿
            │
            ├── 安装计划 ── 完整版本/代码/ABI验证 ── 必要原生早装入口
            │                                    │
            └── 需求计划                    完成回调/健康/epoch
                   │                             │
                   └─────> HostTickSequence <─────┘
                              │
              前drain → 命令 → 后drain → 采集 → 维护 → 发布
                                          │          │
                                  不可变观察      插件事件/UI
                                          │          │
                                          └── SDK ───┘
                                                │
                                      显式提交播放操作
                                                │
                               单一PlaybackSession +真实writer

退休owner独立继续维护；诊断/磁盘工作消费DTO副本；解包/转换/索引在游戏外。
```

这是目标图。现有路径及行号见 [审查报告](../../docs/2026-11-17_runtime-redesign-report.md)，不能将图当作已经接线。

## Components

### Loader 与入口桥

- **Responsibility**：配置 Standard/Developer；完整启动验证后建立原生入口；每 accepted host tick 只验证窄的通道条件，执行具名阶段。
- **Inputs**：已验证 profile、包清单/激活意愿、实际 native frame/epoch/health。
- **Outputs**：host tick sequence、模块计划、分阶段计时和公开状态。
- **Dependencies**：现有 NativeFirstInstall、RuntimeBridge、ToolboxHost；对应 SPEC runtime-modes、tick-admission、diagnostics-and-budget。

第一批保留现有 Doorstop/早装链。不能在没有原首次执行时序证据时换成任意晚装加载器。完整安装证明与当前通道状态是两个不同接口；Standard 的窄入口不再声称每 tick 证明全部机器码未被改写。

### Hosting、Core 与模块计划

- **Responsibility**：先发现有效清单和 desired 意愿，再求能力/模块需求闭包；保留现有包隔离、故障依赖撤回、scope 和 mutation 仲裁。
- **Inputs**：候选清单、硬包依赖、RequiredCapabilities、明确的可选需求、场景/局事件。
- **Outputs**：模块 Dormant/Waiting/Active/retiring 状态和 generation-bound 服务。
- **Dependencies**：PluginPackageLoader/PluginHost/PluginScope/ModuleManager；对应 SPEC demand-and-scene、retirement-and-faults。

当前 Loader 先创建 Adapter 再加载包，需拆出纯 metadata discovery。需求来自启用意愿，不等 capability 已 Available 才产生。ALC 继续用于依赖身份隔离；不作为恶意代码沙箱。禁用不等于 DLL 已回收。

### Adapter 观察与共享后端

- **Responsibility**：对每个 host tick/采样区间只采集必要的当前场景/局对象，生成带来源时间的不可变状态；UI、音频、外观等由唯一后端按显式频率维护。
- **Inputs**：已处理的 boundary sequence、需求、场景/run/source generation、现有原生对象。
- **Outputs**：只读 revision、模块事件、目标/资源租约和失效通知。
- **Dependencies**：GameEventState、各模块及已有服务；对应 SPEC shared-observation、demand-and-scene、retirement-and-faults。

频率使用 EveryHostTick、Interval、SceneDirty、OnDemand 等明确策略。移除 camera 用极小正 double 表达逐 Tick 的调度技巧；空 inventory 也受发现间隔约束。音频三个 capability 共享一个实际 Refresh owner。全局 FindObjectsOfTypeAll 的原生扫描不能靠返回后的游标假称限时。

### PlaybackSession 与操作入口

- **Responsibility**：唯一管理该局音源、Clock 与实际写入；将安装验证、操作验证和本地身份复核分层。
- **Inputs**：带 owner/期望会话/动作/参数的请求、真实 roots、private tickets、native writer/source/scheduling fences。
- **Outputs**：不可变观察、命令终态、实际确认值、修改记录、需保留的退出责任。
- **Dependencies**：保留可用的 StockRootedSource/Clock/native writer 实现，逐项替代递归 Provider→BoundSource→Probe 包装；对应 SPEC playback-operation。

验证分工：

| 边界 | 承担工作 | 不向下传播的工作 |
| --- | --- | --- |
| 安装/明确安装变更 | 固定构建、完整代码/页/原跳板/成员合成证明 | 不让每个数据 getter 再做同一整图审计 |
| 当前操作 | 主线程、实际注册会话、对象/根、活动状态、Clock和真实writer资格、写前/写后readback | 不通过 Probe 重新申请整套 capability |
| 内部包装 | 相同 issuer、局/源代次、scope未结束、当前receipt引用 | 不重新捕获source或产生新的执行权限 |
| 诊断 | 显式运行完整审计并报告范围/成本 | 不自动授予新票、不把结果作为长期验证缓存 |

Standard 只对支持的已知来源和登记变更提供相应保证，不承诺捕获任意外部机器码/内存修改。native original、writer reservation/nonce、completion、sticky Unknown 和在途 roots 不能为了减重替换成公开数字或成功布尔值。

### 诊断和外置工具

- **Responsibility**：对入口、host、模块和命令计时；限流、合并与异步持久化已复制数据；资源解包/转换等离线执行。
- **Inputs**：普通 DTO、具名阶段计数、诊断请求与显式启用的 proof。
- **Outputs**：当前模式和保障边界、分阶段成本、当前包验收记录。
- **Dependencies**：复用 StructuredDiagnosticLogger、CallbackPerformanceMonitor、现有 tools/scripts；对应 SPEC diagnostics-and-budget、deliverable-scenarios。

状态生成不调用游戏服务的实时 getter。主线程只复制有界样本，排序/完整序列化/磁盘写移到后台。预算不能抢占正在运行的同步代码；已经执行过的回调也不能因超预算而被标作未发生。

## Data Flow

1. 读取模式、纯清单和启用意愿，建立必要早装入口及模块需求计划。
2. 实际完成的指定 Tick 通过窄入口检查，获得 host tick sequence。
3. 前 drain 消费已有 boundary；执行有界数量/预算的待执行工作；后 drain 消费命令新产生的 boundary。
4. 有需求且到期的观察采集一次，维护共享后端与真实 pending owner，发布不可变 revision。
5. 插件读取状态或处理事件；显式写命令进入当前会话的唯一操作入口。
6. 操作实时验证后执行真实原生动作、读回和确认；未知终态保留，旧会话视图立即失效。
7. 停最后消费者先撤新请求，退休阶段等待实际原生退出后释放。诊断异步消费 DTO。

## Tech Stack

| Layer | Technology | Reason |
| --- | --- | --- |
| SDK/Core/Hosting/Adapter | 保留现有 C# 与产品 target framework | 已有服务、依赖、scope和仲裁，不为减重迁移语言 |
| Runtime/native | 保留现有 IL2CPP、Windows x64 桥与必要早装入口 | ABI/原函数与生命周期责任需要真实约束 |
| Tests | 保留现有受控 managed/native 夹具，增加边界与成本计数 | owned机制、静态游戏事实和真实游戏分别证明 |
| 外置工具/文档 | 现有 PowerShell/Python/Markdown | 游戏外处理资源和验证资料，不增加游戏常驻工作 |

## API Contracts

以下是计划中的语义契约，不是现有 SDK 新增声明；不引入 HTTP/RPC。

| 操作 | 输入/输出 | 约束与错误 |
| --- | --- | --- |
| ReadObservation / Observe | scope→不可变状态、observedAt、revision、可用性/原因 | 无隐式扫描/准备/写入；见 SPEC shared-observation |
| DeclareDemand / ReleaseDemand | 有效启用意愿或scope内需求→需求token | 无同步native初始化；Waiting需求有效；见 demand-and-scene |
| SubmitPlayback / ReadCommand | 动作、值/单位、期望会话→命令ID/Queued/终态 | 真实执行前核对当前会话；见 playback-operation |
| RequestDiagnostics | 显式诊断请求→有界记录/完成状态 | 不阻塞普通帧；见 diagnostics-and-budget |

命令ID是追踪标识，公开的 run/source/generation 数值和观察 DTO 都不是原生执行凭据。旧同步控制 API 若更换为提交式接口，所有同候选示例/模板一并更新，不维护旧编译插件桥。

## External Dependencies

| Dependency | Purpose | Version |
| --- | --- | --- |
| .NET、Doorstop/BepInEx bundle、Dobby及原生helper | 当前构建与启动依赖 | 以 [dependencies.lock.json](../../dependencies.lock.json) 及当前包manifest为准；本批不升级 |
| MuseDashMirror / SiraUtil | 游戏专用库、按上下文装配服务的参考 | 只读参考，不加入产品依赖 |
| Geode | Hook相对顺序、订阅生命周期参考 | C++/不同游戏，不直接移植 |
| osu!/Phira | 时钟职责与练习重建范围参考 | 完整播放器有宿主控制权，不证明Rizline存在同等接口 |

参考来源及明确限制见 [审查报告](../../docs/2026-11-17_runtime-redesign-report.md)。本批未下载执行第三方代码。
