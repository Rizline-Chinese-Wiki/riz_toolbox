# 同源 Canvas 缓存与位置组合：本批离线验收

正式 `RunValue.PrepareSeekCanvasBasis` 使用同一次实际 CanvasSource / Capture / Plan，先写入并确认完整缓存，再执行实际位置命令；显式恢复按位置、缓存的顺序进行。**这里只确认有序的局部值观察，不开放完整 Seek，不声明 Unity effects、native producer/consumer 或真实帧级发布已经闭合。** 未安装或启动游戏。

| 检查 | 结果 |
|---|---|
| 六组件 Release / solution | 构建成功；准确警告及错误数见原始日志和索引 |
| 新同源组合 / 原判定 | 16/16 + 300/300，生产DLL/PDB相同的组合覆盖；新16组逐组进程、原300另进程，未运行当前单test DLL的默认316 |
| Runtime ABI / 精确静态读 | 51/51、6/6 |
| 完整 AudioContent 默认 | 1001/1001 |
| 输入 / 依赖 | 当前856项与恢复前后一致；原01..11测试图仅Guards断言一项不同，生产12产物字节全等；当前24份依赖匹配 |
| 独立源审 | 58 项范围/哈希匹配，无剩余必要阻断；未逐行支持依赖另列 |

实际命令、自然退出与哈希见 [新图结果](build-test-result.json)，source-only复核见 [独立审查](independent-source-review.json)。源码审查不代替执行，运行器的原sourceReviewComplete字段与后补源审状态分别保留。

这是分图续跑的组合覆盖：Cases01..11绑定原测试程序集/输入图；仅修Case12的独立handle断言后，六组件DLL/PDB共12文件逐字节相同，Cases12..16及原300/51/1001/6使用修正后测试图。第一次oracle把NowTime/Speed错记同槽的失败和第二次把申请方Run额外根误算进source的失败均保留；原review也保留并明确漏审。中断前Cases12/13已自然通过，14只启动无退出记录；核原进程消失、当前856输入和产物未变后，从14续跑，原缺失exit不编造。指纹获取时点和[12产物比较](production-product-comparison.json)、[恢复审计](interrupted-recovery.json)分别记录；原历史dotnet/runtime二进制哈希未采集，不宣称跨中断历史环境完全相同。原/当前测试DLL和相同生产DLL/PDB见compiled-provenance，不将旧测试程序集冒称为新程序集执行。

一个managed holder只保存bootstrap及原两child lease，不建立第二套物理源或重算Plan；同源身份用ReferenceEquals核验。managed独占scope拦截sibling同步重入，caller-local撤销穿透完整NoteView读取，明确撤销token不把健康sibling的source误标expired。真实来源/ABI漂移和unknowncleanup仍sticky。独立child回调Dispose aggregate时，最后获准释放的source terminal observer只做managed registry收尾，不读busy child journal。

两原公共owner与不可变journals不替换。合并旧成功用child observation revision失效，恢复前联检完整expected，失败不会自动逆写。position任一setter进入就保Unity外部效果和完整来源根，读回或原始位恢复不会消除此责任；cache确认只关闭自己的storage责任。未知临时/最终free保原publication/source与child/aggregate owner，不重试。

位置和缓存的双样本/最后条件冲突携带已经取得的完整或部分raw-bit观测；pending exactphysical槽只允许它的before/target，其他任何已观察槽都必须匹配expected。样本保留范围是typed冲突及其已完成前缀；普通IO/metadata异常没有新增通用部分采样日志，不凭异常本身推断第三态。回归同时覆盖拒绝第三态和允许合法pending恢复。新的测试fixture只让借用元数据名称按当前text保持稳定地址；owned字符串仍正常free，根/boxed结果及未知责任不做合成释放。

| Evidence | Finding | Path |
|---|---|---|
| E-001：[执行](build-test-result.json)、[冻结输入](inputs-after.json) | F-001：新图功能矩阵通过 | 同一次capture/Plan → 两原owner → cache读回 → position → 联合观察/条件恢复 |
| E-002：[独立源码审查](independent-source-review.json) | F-002：共享scope、取消、cleanup及所有已观察冲突归属已复核 | 准入/physical lease → sticky样本 → 原owner责任 |
| E-003：[迭代索引](evidence-index.json)及iterations | F-003：首轮中止和typed冲突RED保留 | 源审定位 → 双boundary自然exit1 → typed修复 → E-001 |
| E-004：[静态合同](cache-static-evidence.json)、[实现合同](implementation-contract.md) | F-004：复用冻结字段/方法依据，不声称真实帧协议 | actual ordinal source → 缓存/time/speed先于显式setter |

P-001(callflow)：E-004固定来源 → E-002唯一capture与三managed holds → E-001完整预检与顺序执行 → 已进入effects/storage原journal → 联合选定值恢复 → 原owner保责。观察不是原子CAS，也不排除未观察ABA。

复现使用不存在的新目录：

```powershell
C:/Python314/python.exe -B work/canvas-basis-validation-20261112/run_validation.py --output D:/riz-build-cache-next/canvas-basis-20261112/manual-full
```

`--basis-only` 与 `--basis-conflict-only` 是迭代专项，不能替代完整矩阵。当前实际目录 `D:\riz-build-cache-next\canvas-basis-20261112\root-r3\recovery-r1`；全部历史结果/失败按原输入保留，不能用旧包验收新源码。[下一line/ring来源合同](next-line-ring-source-contract.md)只交接待实现任务，不是完成证明；缺失runtime、renderer consumer/source、Hold引擎退出与完整时钟事务也仍待实现。B01–B19未整体完成，真实Speed/Seek关闭。未操作UI/账号/代理、未改永久环境/旧ZIP、未Git提交推送。
