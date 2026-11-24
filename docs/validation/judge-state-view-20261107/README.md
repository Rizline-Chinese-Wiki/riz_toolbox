# 判定队列快照离线验证

`JudgementRunCapture.CaptureSeekJudgeStateView(profile)` 已合入源码，提供内部只读基线，供后续完整 Seek 状态重建使用。它独立持有实际 RunValue 对应的 Note 视图、Judge 和三条 List，保存原顺序、重复引用、跨表别名及完整 backing capacity；仅活动槽按实际引用连接 Note，尾槽仅保存存储证据。版本与 hold 计数保留完整有符号 Int32。

捕获取两次完整快照，`VerifySource()` 重新检查基线；已观察变化会永久撤销旧视图。同一 RunValue 的递归捕获在 reader 构造前拒绝，门闩在成功或异常后复位。回调 Dispose、错误线程、局修订和未知释放责任均纳入本次验证。

已验：新 Runtime 188 / Adapter 273 个源码文档对应各自 PDB，产品编译零警告、零错误。正式合成入口实际通过 16 组、22 fixtures、70 子例、545 检查，compiler / metadata / cases 三个原进程均自然退出 0。测试宿主保留 4 条已知 net6/net8 CS1701 引用统一警告。

完整报告为 85.4 MB，超过事先选定的 32 MiB 输出限额；因此原监督与 outer 以 1 退出，原失败记录保持。独立补验核对当前源码、程序集/PDB、原进程退出及完整最终结果后接受行为证据，没有修改限额、改写旧回执或重跑场景。注入故障保留 5 个未确认 publication 责任和 3 个 unknown-free owner；合成 handle 表归零不代表这些责任已解除。

这不是公共 SDK 新接口，也未开放完整 Speed/Seek。相等采样不保证原子性、不排除 ABA；hold 计数为 0 不证明协程闲置。游戏未安装或启动，实机验收仍待完成。本批整合后的六组件及今日交付结果以 CURRENT-CHECKPOINT 最新段和交付包 manifest 为准。

证据与当前四源的 SHA256 见 [evidence-index.json](evidence-index.json)。
