# K 批范围

2026-10-26。目标继续为 B01–B19 的 PC Mod 公共底层，不缩减为本批回归。

- `auth`：用户授权持续实现、使用多代理与测试；游戏操作遵循后续“先继续代码，实测稍后”。
- `in_scope`：本地源码；三类协程 tracker 的逐记录观察根回收；真实来源冻结死亡事实及原 factory AfterClose 释放；Stop 物理 scope 到实际 Detach 代次的关联；受控托管与既有自有原生夹具回归；独立缓存归档。
- `out_of_scope`：本批向游戏安装、运行或停止游戏；将受控来源作为生产许可；以 Stop 返回、Detach 或 Release0 代替全部消费者完成。
- `network_profile`：本批实现和回归仅本地进程及本地文件；不向外部服务发送信息。
- `completion_boundary`：K 批通过只证明声明的受控路径。生产 admission、内联清理、scheduler、async wait、所有消费者封口与完整资源退休仍需对应实现与证据。

沿用 I 批固定目标与原件，见[上批范围](../foundation-engine-production-20261025/scope.md)。真实游戏目录本批不写入。未提交或推送仓库。
