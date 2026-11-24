# Doorstop 进程状态证据包

native 修复已在限定的 Root build + header 8 项 + 实际 Root DLL 6 项范围内接纳。SDK 游戏启动未验收，目标未完成。运行记录第三次实际存在 Unity tick bind，随后发生模块 manifest/interval 异常；勿写作“没有 tick”。

本目录可整体搬走；ZIP 中还包含本说明、checker、报告、scope、timeline 和全部证据。`hash-index.json` 固定 ZIP 内除索引自身之外的每个成员，`archive-receipt.json` 在 ZIP 外固定 ZIP 及索引哈希。索引来源字段仅作历史出处，不需访问原盘。

```powershell
python ./check_evidence_archive.py ./doorstop-process-state-evidence.zip
```

可另加 `--expected-sha256` 与外部可信 archive receipt 对照。checker 仅读取 ZIP 和其中的 LGPL 对应源码 ZIP；不解压、不构建、不安装、不运行任何 DLL/EXE/历史控制器，不访问 Root、D 缓存、游戏或网络。

证据入口：`summary.json`、`case/report.md`、`case/scope.md`、`case/timeline.md`。实际 Root 构建在 `root-build/`，r2 DLL 夹具在 `root-native-fixture-r2/`，原始八项在 `header-fixture-r2/`；旧验收及失败保存在 `root-application/acceptance-r1.json` 与 `limited/`。三次历史运行观察在 `runtime/`，只作限制和后续定位依据。

原始控制器保留绝对路径及防覆盖守卫。归档校验可独立运行；完整历史重跑需匹配 Windows/MSVC/SDK/PowerShell 和构建输入，不属于本包的离线校验。包不包含游戏二进制、用户配置、全部缓存或无关管理代码。
