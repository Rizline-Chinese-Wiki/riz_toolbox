---
plan: runtime-redesign
version: 1.0
created: 2026-11-17
status: active
---

# runtime-redesign

> 将现有 riz_toolbox 收敛为轻量的 Rizline Mod 平台：保留公共能力，重新划分安装、Tick、会话、操作和诊断边界。

**首批源码已落地。** Standard/Developer 配置、Tick 通道准入和首批按需模块已接入。当前实机状态和待完成项以[续接点](../CURRENT-CHECKPOINT.md)为准。

## Documentation Index

| File | Purpose |
| --- | --- |
| [PLAN.md](PLAN.md) | 目标、边界、交付阶段与尚待验证的决策 |
| [SPEC.md](SPEC.md) | 正常/开发模式、Tick、观察、需求、退休、播放和交付行为 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 现有项目职责、目标数据流、运行接口和迁移边界 |
| [CHECKLIST.md](CHECKLIST.md) | R01–R08源码任务、依赖和SPEC验收入口 |
| [审查报告](../../docs/2026-11-17_runtime-redesign-report.md) | 当前真实调用链、源码证据、旧实测边界与参考项目 |
| [设计基线](../../work/runtime-redesign-20261117/source-audit-r2.json) | 重构开始前的源码和调用位置 |
| [运行模式](../../docs/Runtime-modes.md) | 当前模式、发行包和按需能力用法 |

采用T2：此次只做运行架构与交付范围；不新增前端、数据库模型或规划扩展。用户已委托自主设计，内部档位和常规实现取舍由维护者决定。

## Quick Start

1. 读 [SPEC](SPEC.md)，先接受“完整安装证明”和“当前可执行性”是不同合同，Standard 不承诺任意外部内存修改检测。
2. 依照 [CHECKLIST](CHECKLIST.md) 续接当前未验收项，使用同一候选的构建和运行结果。
3. 普通版和验证版的实际开关见[运行模式](../../docs/Runtime-modes.md)。

## Current Status

R01 和 R02 已有联合离线候选。R03 已接清单发现、硬依赖需求和八个按需模块，音频共享后端、组合输入和实验播放仍需梳理。保留 readback-provider-r3 作为实现前基线。

首先交付可正常游玩、基础插件可组合使用的预览；暂停倍率作为独立操作垂直迁移，任意Seek在状态重建闭合后单独开放。完整B01–B19与production admission没有因此完成。

## Decisions

- 保留 SDK/Core/Hosting/Runtime/Adapter/Loader 六项目，不以合并文件或换语言作为减重手段。
- 标准运行、作者开发包和验证资料有明确边界；已有用户文件和研究证据不删除。
- 基础场景观察持续工作，其余模块按需求/场景激活；必须早装的native入口不伪装成可以随时晚装。
- 观察仅提供数据，命令单独执行；写前保护、实际readback、Unknown不重试及pending roots必须保留。
- 计时包含Host之前的入口成本；预算和catch均不承诺抢占同步native调用或隔离进程崩溃。
