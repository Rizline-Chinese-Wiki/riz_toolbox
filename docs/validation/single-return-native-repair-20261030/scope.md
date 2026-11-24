# 范围与授权

2026-10-30，续接已授权的离线 Single-return prototype 修复。只使用自有 Windows x64 helper、fixture、Runtime bridge 及 N2 publisher；普通技术验证，`flavor=null`。允许在 `D:/riz-build-cache-next/` 新建互不覆盖的源码、构建及证据目录，并在项目 `docs/validation/` 记录结果。

本次没有安装、启动或操作真实游戏，没有操作账号、凭据或撤销的 B 账号。正式 FirstInstall V7、83 exports、26 operations 和发布候选未被本次修复改写。没有复跑旧 17 cases / 328944 checks 的 native 矩阵。

修复对象是独立 `SingleReturnContext.cpp` 的候选值域检查和候选算术 SEH 边界。Runtime/Publisher 现有 fault 及私有 publication receipt 清理逻辑经实际联合验证后保留原样。没有修改其他 Gameplay Clock 源码或 snapshot 脚本。

本报告只证明受控离线原型及列出的实际源码、PE/PDB 和机器用例；真实 Speed/Seek、游戏 detour、实际 pitch/schedule/source observer、音频同步恢复及生产准入仍未实现或未验证。完整产品范围和全树冻结由根任务继续处理。
