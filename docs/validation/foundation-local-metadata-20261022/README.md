# LOCAL 原生 metadata 构建与安装

2026-10-22（UTC+8）最终包通过 **25 套、846/846** 检查，Runtime92、AudioContent100。两个原生 fixture 分别通过 31,261 / 61,358 项。最终依据为 [build.log](build.log) 和 [build-summary.json](build-summary.json)；`build-before-owned-argument-thread-fix.log` 是修复 owned Arg 线程清理前的中途版本，不代表本包。

本次最终日志仍包含 **14 条不同的 CS0649 编译警告**，均来自 AudioContent 测试链接的 FakeNative 字段未赋值；完整原文保留在摘要的 `compilerDiagnosticLines`，不能写成零编译诊断。没有编译错误，全部检查通过。

## 本包变更

- `NativeRuntime.WriteExact` 区分引用和值，验证精确声明类、字段类型、实例可写性和 ABI，通过官方 IL2CPP field setter 写入；在线程准入前不消费参数。
- `Arg.Dispose` 在丢弃拥有的 root 前验证所属线程，错误线程的 Dispose、Invoke、NewExact 失败保留调用方清理能力；相应回归已加入 Runtime92。
- `NativeLocalMetadataFactory` 创建并持有独立 Level、各 info、闭合颜色字典、两个 list、LevelDifference 和 LevelMetadata，共每 owner 10 条 root 记录；身份由 namespace/song/revision/chart 独立保存。
- `local-native-metadata.enabled` 显式启用一次 metadata-only 探针。探针创建三个 owner，经游戏原生 GC 后读取九个 getter/颜色，验证跨 provider 同标题、同 ID 不同 revision、独立撤回及清理。

## Evidence → Finding → Path

| Evidence | 记录 | 已核验内容 |
|---|---|---|
| E-build | [完整构建](build.log)、[摘要](build-summary.json)、[原生 fixture](native-build/) | 846/846、25 套和两个原生 fixture；日志 SHA-256 在摘要中 |
| E-package | [包清单](package-manifest.json)、[安装核对](installation-check.json) | 18 个实际安装文件 SHA-256 与本包一致 |
| E-source | [源码清单](source-manifest.json)、[核对结果](source-integrity-check.json) | 构建对应的 283 个源码输入未变 |
| E-original | [原文件 before](original-game-before.json)、[原文件 after](original-game-after.json) | Rizline.exe、GameAssembly.dll、global-metadata.dat 三个原游戏文件未变 |

F-build（validated，E-build、E-source）：本包具备已检查的字段写入、线程拒绝、普通 metadata 图及有界 owner 清理行为。此结论是离线实现验收，原生 fixture 不代表游戏内全部 ABI 或生命周期验收。

P-verify（callflow，E-build、E-package、E-source）：最终构建与检查 → 冻结包/源码输入清单 → 安装 → 独立读取目标文件 SHA-256 → 与清单逐项比较。复核当前安装和源码时，在仓库根目录执行：

```powershell
python docs/validation/foundation-local-metadata-20261022/summarize-build.py --installed
```

实机证据单列于 [metadata 探针目录](../runtime-local-metadata-20261022/)。每份证据只适用于其冻结包与会话。普通 metadata owner 的 `CanLaunch=false`，LOCAL RequestPlay 仍返回 OperationUnsupported；完整本地曲包的启动、重试、结果和撤回责任仍须实现，B15 保持未勾选。完整 B01–B19 仅 B03 整项与 B18a 子项已勾选。
