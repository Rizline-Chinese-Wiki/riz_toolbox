# Publisher 日志目录修正的 root 复核

原独立审查绑定 `publish.py` SHA `0b20b50c8710730071cc3736da17302a4734ab766340e9640b96037745f888c5`。原文件完整保存在 `work/line-dynamic-consumer-evidence-20261113/iteration/frozen-publisher-v1/`。

root 在真实 shadow 完成后执行只读祖先与 shadow 子审计，发现 live view 将日志解析为 `root-r2-snapshot/build.stdout.log`，而实际冻结布局为 `root-r2-snapshot/logs/build.stdout.log`。该缺失导致审计实际 exit1，没有写归档或修改结果。

最终 publisher SHA 为 `844ffcb216c06fd8fe09dbbfc23252b0e075640fc43845fcffd1137b8e38f573`。唯一改动是在 `View.shadow_log` 的 live 文件路径加 `logs` 目录；归档路径、原日志 SHA、退出和内容检查、来源/二进制/显式 root pin、数量和原子发布门禁均未修改。

修正后同一只读子审计实际 exit0，核对已接受 895 图、完整 v4 候选与真实 shadow 13 步骤/12 专项及原句柄退出链。主图 898 正式验证仍在进行，本复核不认证主图通过或已经发布。
