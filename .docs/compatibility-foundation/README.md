---
plan: compatibility-foundation
version: 1.0
created: 2026-10-19
status: active
---

# compatibility-foundation

> 让独立 Mod 能通过稳定契约接入 Rizline，并明确共存、停用和游戏版本适配的边界。

## Documentation Index

| File | Purpose |
|---|---|
| [PLAN.md](PLAN.md) | 目标、范围与里程碑 |
| [SPEC.md](SPEC.md) | 详细行为、失败分支与验收 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 分层、依赖与运行模型 |
| [CHECKLIST.md](CHECKLIST.md) | 可执行任务与实际状态 |

## Quick Start

1. 先读 SPEC 与 CHECKLIST，按模块分工实现。
2. 在仓库根目录执行 `pwsh -File scripts/Build.ps1`。
3. 通过隔离安装检查后，在授权的本机 Rizline 构建验证并回写证据。

## Current Status

M1–M3 代码实现和离线检查已完成，M4 已通过插件包、真实资源覆盖恢复与安装验收。用户 2026-10-19 15:22 的游玩已验证场景切换和失败结算通知，但开局通知漏报，完整流程尚未通过。Boolean 解码修复和控制器诊断已于 15:32 安装并核对包哈希，等待实际游玩复测。当前状态以 CHECKLIST 的勾选和证据为准；启动证据位于 docs/validation/v02，实际游玩及补丁安装证据位于 docs/validation/v02-gameplay-20261019。下一阶段公共底层的覆盖和架构见 [补全计划](../foundation-expansion/README.md)。
