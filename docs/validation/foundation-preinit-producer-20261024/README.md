# 原生启动安装与托管采用

2026-10-24。**1515/1515、25 套（Runtime160、Hooks171、Audio573）通过，源码与新包已冻结，未安装。** 实际 Doorstop pre-init 路径已接原生 producer；先校验固定游戏与 Dobby 身份，准备 runtime-invoke、UnityEvent 父入口和内段 relay，再按固定顺序发布三处写入。CLR 采用已存在的身份和 epoch，不重新安装这两个通道。

## 这轮实际增加了什么

| 层 | 已实现/已验证 | 尚缺的联合验收 |
|---|---|---|
| 原生启动 | Doorstop 在 original il2cpp_init 前调用 producer；Ready 后只调用原 init 一次；返回0不启动 CLR，原异常不捕获 | 真正游戏冷启动、时序和异常分支 |
| 首次发布 | 固定 hash/PE/字节/unwind/ABI/实际 init 身份；三处写入、护页恢复、跳转读回；重入转 Unknown | 游戏首次执行前独占合同的实机证据 |
| 托管接入 | 生产 RequireReady、runtime-invoke Adopt 与 broker bootstrap 路径已编译；4 组 ABI/拒绝回归 | 实际 producer 与 CLR 在同进程采用、生产 callback |
| 交付 | 22 项包，修改版 Doorstop 对应源码和 LGPL 文本齐全；allocator 避开已认领槽位 | 干净环境复现、装入游戏和升级矩阵 |

真实原生 runtime-invoke fixture **274 断言**，12 次原调用、8 次 After、2 个跨 Attach 的旧调用不观察，原生/observer SEH与C++、worker、递归及 full-pointer 参数返回检查通过。写前拒绝 **5 个独立进程/100 断言**，均零游戏写入。

实际固定 GameAssembly 在独立进程用 `DONT_RESOLVE_DLL_REFERENCES` 映射：**175 断言**证明真实 producer 的父入口、runtime 及五字节 CALL/relay 三处发布。未执行 DllMain、游戏 init 或任一游戏函数。Ready 读数为重入前的 **phase3/unknown0/writes3**；再次 Before 返回2且内存不再写，最终为 **phase4/unknown1/writes3**。两种时间点分列；本例不证明游戏运行或首次执行屏障安全。

真实 Doorstop 冷启动时序、生产 CLR 回调与 IL2CPP/listener/GC 尚未验收；全部 listener/future 来源、连续 mutation/provider 转交和取消、全消费者资源退休及完整 Retry/Return/Loader 仍缺。RequestPlay 保持关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。

## 证据与复现

474 项源码与 22 项暂存包逐项冻结；较 1511 新增21、修改11、删除0。源码 ZIP `89cdc781791f9c8ccfa42a5a69c201ef57661814d79d2b63b141cc6367cfd039`，包 ZIP `4c7939c1beab080b40194b346bbcec62f7a545eb2f8ef2761a07a403099e3740`，完整日志 `4ad71734738a8e6b5640d1f8cc10609c66f6c0298379c2c9a647f39c9f996418`。旧1021安装18项、其326项归档与原游戏三文件哈希匹配。 完整构建保留 108 条去重编译诊断（包括 upstream native warning），详见 build-summary；不宣称零警告或干净环境复现。

仓库根 PowerShell7：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/preinit-producer-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`。省略 ValidationGameAssemblyPath 时不会运行真实映像正向检查；不能引用本批175断言作为那次构建的结果。

[完整日志](build.log)、[汇总](build-summary.json)、[runner](build-run.json)、[首轮失败](build-first-parse-failure.log)、[runtime 原生](native-runtime-invoke-evidence.json)、[5拒绝](native-rejection-evidence.json)、[映像发布](mapped-publication-evidence.json)、[Doorstop](doorstop-evidence.json)、[源码归档](source-archive-check.json)、[包归档](package-archive-check.json)、[与1511差异](changes-from-1511.json)、[有限交叉审查](review.md)。首轮 mapped 验证脚本解析失败；修正并补强重入、Unknown 和 init 失败分支后重跑全量，最终 session7957 exit0。独立 Doorstop 工具链首次失败及成功日志也保留。

仅推进代码和离线验证；无安装、游戏/UI操作、进程终止、原文件修改或commit/push。游戏仍旧1021包。静态23范围/1706指令/6E8与三输入hash再核[结果](terminal-evidence-check.json)，[来源](static-evidence-origin.json)单列。

## 下一步

先做[真实 CLR 联合采用及冷启动证据](next-integration.md)，再推进 listener/future 与完整消费者退休。完整[B01–B19](../../../.docs/foundation-expansion/SPEC.md)仍 active，仅 B03 整项/B18a 子项勾选，实测稍后。[授权](scope.md)、[正式报告](../../2026-10-24_底层-原生启动安装-report.md)。
