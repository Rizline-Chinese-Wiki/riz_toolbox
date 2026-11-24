# 当前候选的设置页与失败结算验证

当前精确1172候选已在真实游戏中完成一次失败结算闭环：原结果返回被接受，开局、RunEnded和RunFinished关联同一RunId，SDK记录的分数0、完成率0、Bad0、Miss479与实际RESULT页一致。原游戏结果字段为`Crashed=true`、`Skipped=false`、`EndReason=Failed`；这里的Crashed是单局失败字段，不是进程崩溃。本轮不证明Completed正常通关。

用户已授权必要游戏操作，并在前台接手后回复“跑完了”。这是普通功能验证，flavor=null。游戏进程25008于2026-11-15 13:16:06启动；诊断会话`20261115T051638374Z-892aaffc8645488f9462136b3308845c`。观察使用先前已安装的相同23文件候选，没有另建或换包，安装文件及GameAssembly哈希在两个阶段均一致。当前宿主SDK1.2.0，示例面板旧0.2文字不参与身份判定。

| Evidence | 当前事实与SHA-256 | Finding / Path |
|---|---|---|
| E-01 [设置页观察](validation/game-result-flow-20261115/phases/settings-page/observation.json) | `dc3f19cfa1db2b7449ceeacb1aea572c86fffb2015d7629059992c207d395c1e`；实际设置页read/override两模块active；界面流速7.3、偏移-22，只读未修改 | F-01：实际设置页可使对应模块就绪；design / validated / high；P-01 |
| E-02 [结果页观察](validation/game-result-flow-20261115/phases/result-page/observation.json) | `f182b9d4a78bc297ef59614dbe6c4330a12d6c280efbeb1b4ac0855c4935090f`；Greatness Earth EASY4/FREE，0/479 Hit、0 Bad、479 Miss、0/1011400 Score | F-02：本候选的真实失败结果与界面一致；design / validated / high；P-02 |
| E-03 [完整本会话日志](validation/game-result-flow-20261115/phases/result-page/toolbox-current-session.log) | `de29b71eebef901d58afd144a2394e0d6c83574252031f8be8b71adb2c05860e`；原ControllerOver accepted=True，RunStarted/RunEnded/RunFinished同RunId，结束原因为Failed；无框架Error | F-02的原始来源与关联依据；P-02 |
| E-04 [归档索引](validation/game-result-flow-20261115/validation-summary.json) | `2c034063df1fc3cc29ea2892efc3237cc9931fe4aa88d31433a9125d07888635`；22文件，含两阶段原始数据、实际安装/构建身份和5份精确参考源码 | F-01/F-02可独立复核；P-01/P-02 |

P-01（callflow）：同候选已安装→打开实际设置页→模块从等待变为active→只读capture核23文件/会话/状态（E-01/F-01）。只证明就绪与页面观察，没有执行SDK设置覆盖、双包仲裁或回滚。

P-02（callflow）：真实ProgressControl播放标记创建RunId`9674fa3a160c412b969673391aa14dcb`→期间五次失焦暂停及恢复→用户保持前台完成本局→06:27:02UTC原ControllerOver返回保留同run来源→06:27:03UTC生命周期RunEnded及RunFinished关联该RunId（E-03/F-02）→root实际观察RESULT并核对字段（E-02）→冻结证据及独立归档checker（E-04）。生命周期session为`8b008e4fc53c42c6aefe7e37f2f5a589`，与宿主diagnostic session是不同标识，没有混用。

只读复核命令，在仓库根目录执行；仅读取本归档，不访问原D盘来源或运行游戏：

```powershell
python -B docs/validation/game-result-flow-20261115/controls/publish_evidence.py --check-only docs/validation/game-result-flow-20261115
```

发布实际0/488db4，root另一次直接归档checker实际0/2ffbc9。截图通过原电脑操作工具现场查看，归档保留文字观察和原始SDK日志，不复制截图payload、游戏二进制、用户配置或整个编译树。五份参考源码均与本轮完整1172构建清单匹配，不能把这份小归档当独立重建包。

Completed正常通关、精确Back/Retry/Skip当前组合、全部十二判定来源、非零分数/时间校准、B01完整矩阵、设置写入和恢复、进程最终退出及完整SDK仍未验收。当前游戏保留在已核对的结果流程，未自动关闭；旧预览ZIP保持原字节。FullSeek及生产engine权限不因本次结果观察开放。
