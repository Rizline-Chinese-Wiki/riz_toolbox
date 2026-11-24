# LOCAL metadata bridge 静态证据

[完整报告](2026-10-22_逆向-Rizline本地metadata桥-report.md)记录 27 个有界范围、44 个已校验指令锚点和精确 DTO 数据图。

普通 LevelMetadata 可继续作为独立 LOCAL metadata 工厂候选；其 getters 没有目录/资产加载器依赖。原版 LevelOver.Start 内联保存成绩并调用 Steam 成就/统计，stock 重试读取 TextAsset，必须适配这些分支后才能放行完整 LOCAL 播放。

本次仅完成静态提取，runtimeAcceptance=false。未更改 src/tests、构建或操作游戏。

- [范围](scope.md)与[时间线](timeline.md)
- [PE 导入/导出及工具版本](triage.json)
- [原始反汇编](disassembly.txt)、[结构化范围](boundaries.json)、[dump 字段摘录](metadata-excerpts.txt)
- [关键指令校验](static-claims.json)与[证据哈希](evidence-manifest.json)
- [提取脚本](inspect-metadata-bridge.py)、[校验脚本](verify-static-claims.py)
