# 动态发现、输入焦点与安装恢复整合

本批继承前一 691 包的四项修复，新增三组能力修正；此前的实机会话均保持原样，不自动成为新包的实机证据。

| 范围 | 行为与专项证据 |
| --- | --- |
| 原生发现 | 每轮直接读取最多4096项，跨次推进；不保留未持根指针、不按单轮缺席判死。Runtime57/57，见[窗口验证](../visual-admission-20261021/native-discovery-window.md)。 |
| 动态准入 | 满128仍刷已有语义；优先级及同级有界轮换；不淘汰持有中或清理未确认的目标。Visual旧服务59/67，历史截断版67/68，最终68/68。50000历史边界与算法模型区分，见[服务验证](../visual-admission-20261021/service-admission-checkpoint.md)。 |
| 输入焦点 | 采集和派发两次核对焦点；每个回调前核owner+epoch，防止模态关闭、文本失焦或回调重绑重放旧键。UI/input59/59，见[输入验证](../input-focus-capture-20261021/README.md)。 |
| 安装恢复 | 旧字节备份、可校验事务记录、失败回滚、中断后先恢复再读取新包、互斥和用户修改保护。新恢复13/13、既有安全13/13，见[安装验证](../install-recovery-20261021/README.md)。 |

**整合已完成：717/717、25个项目通过，0编译警告；18项安装哈希匹配、252项源码输入未变；记录的三个原游戏文件前后哈希一致，安装journal已提交且无pending。** 完整构建与安装结果由本目录 `build-summary.json`、`installation-check.json`、`source-integrity-check.json` 记录。安装脚本须与专项验证过的 `tested-script-hashes.json` 一致。`original-game-before.json` 与安装后核对分别记录 Rizline.exe、GameAssembly.dll、globalgamemanagers，不能扩大为未记录的其他文件哈希证明。

用户因电量不足要求并行加速，游戏保持关闭。本批没有新实机验证，也不新增 B01–B19 整项勾选。目标数组任意重排、超过128持续持有、大列表逐轮轮换延迟、真实电源中断、完整焦点/输入设备和视觉资源矩阵仍有明确边界。
