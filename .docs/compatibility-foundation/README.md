---
plan: compatibility-foundation
version: 1.0
created: 2026-10-19
status: active
---

# 兼容性底座计划（compatibility-foundation）

> 让第三方 Mod 能通过稳定的 SDK 契约接入 Rizline，把插件共存、禁用恢复和游戏版本适配的边界定清楚。

## 文档索引

| 文件 | 内容 |
| --- | --- |
| [PLAN.md](PLAN.md) | 目标、范围与里程碑 |
| [SPEC.md](SPEC.md) | 详细行为、失败处理与验收标准 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 分层结构、依赖关系与运行模型 |
| [CHECKLIST.md](CHECKLIST.md) | 任务清单与完成状态 |

## 使用方式

1. 先看 `SPEC.md` 和 `CHECKLIST.md`，按模块推进。
2. 在仓库根目录运行 `pwsh -File scripts/Build.ps1`。
3. 离线和隔离安装检查通过后，再到本机 Rizline 实机验证并回写证据。

## 历史状态摘要

M1–M3 的代码和离线检查、M4 的插件包与资源覆盖恢复安装检查均已完成。2026-10-19 实机游玩验证了场景切换和失败结算通知，但当时发现开局通知漏报，随后补了 Boolean 解码修复与诊断包等待复测。

具体勾选状态和证据路径以 [CHECKLIST.md](CHECKLIST.md) 为准（启动证据在 `docs/validation/v02`，实机游玩证据在 `docs/validation/v02-gameplay-20261019`）。后续公共底层的扩展规划见 [底层补全计划](../foundation-expansion/README.md)。
