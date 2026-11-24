# Unity backing 离线增量证据

当前状态：**pending / preparation**。已保留static/旧反例/native-r3 19/runtime16/metadata21/fault5-168/ABI2-2089的窄范围实际结果；八正式SDK、worker新consumer及foundation/符号终态仍未收齐，未合入主目录产品代码。

- [阶段报告](2026-11-03_re-unity-backing-report.md)
- [机器状态](current-package-status.json)
- [case scope](../../../work/riz-toolbox-unity-backing-20261103/scope.md)、[workitems](../../../work/riz-toolbox-unity-backing-20261103/workitems.md)、[timeline](../../../work/riz-toolbox-unity-backing-20261103/timeline.md)
- [15组原件索引](../../../work/riz-toolbox-unity-backing-20261103/evidence/retention-index.json)，共约6.6MB；932输入的原始ZIP包含在case，完整GameAssembly/dump未复制。

所有测试结果都保留原字节、原路径和SHA-256；旧失败不改写。仅ownedfixture与只读静态证据，无游戏安装/启动、UI/账号或游戏代码执行。本包不证明pointer+ID双ABA、未知lower writer、完整Unity generation/GC历史、production Speed/Seek/effects或B01–B19。

只读strict review通过也只表示本阶段case结构/fixity通过，不能替代pending产品验收；最终状态由lead收到后续实际终态后追加。

preparation 只读严格哈希审查：PASS（0 errors / 0 warnings）；[最新审查](../../../work/riz-toolbox-unity-backing-20261103/report/preparation.review-attempt2.json)。全部276个原字节副本另有独立哈希核验。该结果不完成仍在进行的SDK/worker/foundation工作项。

2026-11-03 13:46追加：七ABI watch22/12678和stable SDK27已实际0；source/clip原断言失败已保留，测试合同修补后重跑恢复。新整树foundation-r2、worker仍待终态。新增E-016–E-018，不覆盖原15组或旧review。

13:51追加：新源码28套2636／SDK双作者14／六Release／solution0errors／11PE-PDB1032已实际0，见E-019。SDK恢复和worker仍待终态，主目录未合入。

14:00追加：正式SDK worker41实际通过、consumer/driver自然退出0、失效撤权且七根责任保留，E-020；正常恢复仍未验收，未合主。

14:02追加：managed真实Start→新Clock→释放根链路23实际0，E-021；不代替仍在跑的SDK恢复。新候选47路径差分仅准备，未合主。
# 当前结果（2026-11-03 17:30）

本Unity backing离线增量已完成并精确合主932输入：完整八SDK449项、当前产品整树2636项、SDK分发14项、新主构建0error、短检查37、六组件Release、13PE/PDB对应1052源码。最终[case review](../../../work/riz-toolbox-unity-backing-20261103/report/main-final.review-attempt1.json)为PASS，24组证据；[完整来源与历史失败](2026-11-03_re-unity-backing-report.md)保留。没有安装/启动游戏，不代表完整generation、lower、production、Seek/effects或B01–B19完成。

