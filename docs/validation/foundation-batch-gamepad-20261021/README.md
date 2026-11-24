# 内容批量登记与手柄输入整合包

2026-10-21 07:46（UTC+8）。完整 `scripts/Build.ps1` 已完成，**744/744、25个项目**通过。AudioContent52、UI/input70；其余 Runtime57、Visual68、Game77、Hooks43、Judgements35 保持通过。原生fixture 31,261 / 61,358，inFlight均0；实际SDK引用包、两个生成插件与正式宿主的分发检查10/10通过。

包已安装。`installation-check.json` 核对18个文件全部匹配；`source-integrity-check.json` 核对255项源码输入未变；`original-game-before.json` / `original-game-after.json` 确认 Rizline.exe、GameAssembly.dll、globalgamemanagers哈希不变。安装journal已提交，没有pending/transaction，临时runtime-module-proof配置仍不存在。没有新包游戏运行证据。

本批三项：

1. [手柄数字输入](../gamepad-input-20261021/README.md)：current设备及16个数字按钮，设备状态与键鼠隔离，缺失/故障清除旧手柄边沿，继续沿焦点及回调代次链派发。11个新增用例；真实硬件尚未验。
2. [原子内容组](../local-content-bundle-20261021/README.md)：独立SDK能力、前向依赖/DAG、一次修订和整组租约，格式/预算/作用域/取消/parser与外部依赖复核，消费者图保活。16个新增用例；不等于新增游戏歌曲。
3. [真实解析边界](../local-content-resolution-20261021/README.md)：21个有界函数的静态调用、目录/异步音乐/封面缓存/局内资源引用边界及后续接入顺序。没有安装未经验证的新内容hook。

`build.log` 是完成证据，`build-summary.json` 是机器汇总。`build-interrupted.log` 是上一轮被中断、只完成前部检查的日志，未计为完整通过。子代理因工作区额度错误中断；手柄代码由主代理运行检查，内容事务由主代理补齐，静态提取由主代理整理，不将代理计划当作完成结果。

已保留一条测试代码警告：`GamepadInputRegression.cs:91` CS8602，前一行自定义Check已断言Failure非空，编译器不推断该断言；没有编译错误。此包不新增B01–B19整项勾选。完整判定/结算、GC与实际释放、视觉/输入实机矩阵、本地目录/音乐/封面解析及发布压力矩阵仍按SPEC验收。
