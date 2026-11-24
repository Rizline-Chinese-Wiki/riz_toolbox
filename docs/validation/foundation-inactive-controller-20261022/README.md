# 未激活控制器构建与生命周期基础包

2026-10-22，修正版 **953/953、25 套**检查通过，已安装并完成[真实生命周期探针](../runtime-inactive-controller-20261022/README.md)。Runtime98、Hooks61、AudioContent183；原生 helper/Dobby fixture 为 31,261 / 61,358。完整 B01–B19 尚未完成，LOCAL RequestPlay 仍返回 OperationUnsupported。

本包提供可复用的预制体发现、未激活克隆、延迟销毁责任与 Awake 观测。控制器保持在新建的 inactive parent 下；工厂没有激活接口，不写参数、不修改来源 prefab。后续完整游玩事务仍须负责资源、结果隔离、激活、全局发布/恢复及返回。

| 组成 | 实际行为与检查 | 验证边界 |
|---|---|---|
| 精确静态入口 | 检查 static、声明类、参数、返回、非泛型及固定 Instantiate RVA153FAA0；新增 Runtime6 条 | 不是任意地址调用能力 |
| prefab resolver | 完整扫描普通/Workshop 两个来源快照，总数≤4096；同 prefab 合并、不同 prefab 拒绝；保留 card/prefab 独立 root；14 条回归 | 实机正向来自普通卡片，Workshop 非空来源仍待验；跨帧只复核保留字段，不宣称重新扫描全局唯一性 |
| Awake ledger | clone 身份未知前开捕获窗；按独立 root 匹配、记录前后与帧序；错线程/重入/溢出拒绝验收；13 条回归 | 本次 native 只观察未激活时零调用，尚无实际激活一次的正向时序验收 |
| inactive owner | 新建 inactive parent 后精确三参 clone；独立持有 component/GO/parent/两 Transform 与来源，7 root；11 条回归 | Dispose 只请求 Destroy；后续帧确认所有自有 Unity 对象 dead 才 free；未知结果保持进程责任、配额且不重试 |
| globals 快照 | 原有责任层增加只读完整快照与2条回归，累计14条 | 本次 native baseline 为 controllerSet=false、六字段全 null；无非空 Target、发布、条件恢复或消费者结束证明 |

## 已保存的证据

- [最终 build.log](build.log)、[build-summary.json](build-summary.json)：953/953，最终日志 SHA-256 `72a6977bd5b7254953589f37b49280e26fe0c8a619172632ae89ffd2a4b7a578`。
- [package-manifest.json](package-manifest.json)、[installation-check.json](installation-check.json)：实测收尾的18项安装哈希一致；[303项源码输入](source-integrity-check.json)未变。
- 原始游戏三文件 [before](original-game-before.json) / [after](original-game-after.json) 哈希一致。
- [运行证据](../runtime-inactive-controller-20261022/runtime-proof.json)：775连续事件、746Info/25Debug/4Warning/0Error；严格后续帧10508→10587→10706。

最终完整增量构建无编译诊断。[AudioContent定向日志](array-covariance-targeted.log)仍有15条不同 FakeNative CS0649警告；不能推断干净重建零警告。[首次 Runtime 定向日志](runtime-targeted.log)中的5条失败来自测试夹具没有枚举命名方法，补齐夹具后[98/98通过](runtime-targeted-after.log)，未改生产准入去迎合夹具。

原始951包和只补数组诊断的中间包分别保留在 [before-array-diagnostic](before-array-diagnostic/) 与 `array-diagnostic-*` 文件。两次实机失败详见[原始失败会话](../runtime-inactive-controller-20261022/failed-array-contract/README.md)：实际返回 LevelCardControl[]，原实现错误地只接受声明 Object[]；修正版接受当前来源精确协变数组，仍逐项检查 class/活性/场景/字段。

## 复核

在仓库根目录执行，后续源码或安装变化时会按预期拒绝：

```powershell
python docs/validation/foundation-inactive-controller-20261022/summarize-build.py --installed
```

E-build/source/install/runtime 分别由上述清单与冻结日志支撑。F-lifecycle：固定游戏中的一个自有 inactive clone 已通过实际 GC/读回与延迟销毁，而非只通过受控 backend。P-package：完整检查→冻结清单→安装→显式 marker→菜单进入选歌→验证→正常退出→原样冻结→核对包/源码/原游戏→恢复 marker。

未激活阶段已补证据；完整本地资源 bundle、启动前 pending session/结果门、实际 Awake 发布与条件恢复、完整游玩/重试/结果/返回继续实现。只勾 B03 整项/B18a 子项，不按检查数新增勾选。
