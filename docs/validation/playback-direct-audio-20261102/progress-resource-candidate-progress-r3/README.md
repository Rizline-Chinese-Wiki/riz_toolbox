# 资源连续性候选：离线整合进度

本包保留当前实际完成的构建、行为和源码来源，主目录尚未合入候选。`scope-index.json` 的 `passed=true` 只签保留完整性；`incrementAcceptanceComplete=false`、`formalSdkComplete=false`、`mainApplied=false`。

已完成的准确范围：

- 新产品整树28套2599项与core1039项：原执行为 `foundation-r2`，后续精确测试差异单列继承。
- `foundation-r7`：五个异常释放场景113项（r5原四80＋r7新资源33）；solution实际0错误／56警告；两独立SDK插件14项；六组件Release实际0错误／1条generated profile警告；11对PE/PDB／994份文档。
- `mapped-r3`：metadata149＋四联合场景41／45／45／45＝325项，全部实际退出0。游戏方法／DllMain执行0。
- 原功能矩阵中的11个成功场景237项，包括late首捕获、同局移交、foreign／worker／SEH与普通closed-gate对照。

最后一个SDK场景在原矩阵实际退出1／checks34：测试完成上一事务的Commit／Confirm后，尚未Dispose便发起下一事务，触发了正确的reservation冲突。原日志及失败保留。只在独立 `sdk-current-dispose-r1` 测试副本补显式Dispose，原断言、候选源码、484托管产品输入和48个native输入均不改；该场景仍在补跑，不能签12场景全部成功。

异常释放场景的通过表示未知责任被明确保留且不重放free。资源free结果未知时，host四根继续保留，`cleanupCompleted=false`；没有宣称该异常路径完成清理。测试会保留完整上下文至单次进程结束，避免提前释放仍被引用的内存。

主目录T还在补完原六场景之后的12场景，两轮来源须一起真实闭合。它与新的SDK补跑及独立终态审计均完成后，才能应用候选、核对主目录并继续下一缺口。

本包包含2323份保留原件、913候选源码ZIP、894改前源码ZIP及53路径待合差分。当前候选ZIP尚未含仍待验收的SDK测试Dispose补丁。主目录产品和README原改动保持；原失败、系统关机中断及测试编排错误各自保留，不覆盖成成功。

主要入口：`scope-index.json`、`foundation-r7/foundation-final-summary.json`、`foundation-r7/root-free-test-result.json`、`mapped-r3/final-audit-r4/CLOSE-EVIDENCE.json`。完整generation、未知lower writer、production、任意Seek、effect及B01–B19仍未完成；未安装或启动游戏，未操作游戏UI或账号。
