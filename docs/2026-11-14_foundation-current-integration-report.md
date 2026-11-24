# 当前底层组合：离线验证

当前组合候选保留父批的renderer测试内存修正与Hosting输出修正，承接Hold owner/source/alive/discovery和描述符就绪修正。新图1171完整输入、六组件Release、45项目solution以及95个自然步骤均已通过；27套普通组件2524/2524、Hold62、producer3分别记录。源码尚未合主，完整SDK、B01–B19、完整Judgements/父renderer和真实Speed/Seek仍未由本批验收。

范围来自用户继续离线功能实现的授权与[项目合同](../.docs/foundation-expansion/SPEC.md)；本报告为通用技术文档，flavor=null，没有安全评估case或IOC/ATT&CK。[源码审查](../work/foundation-current-integration-next-20261114/source-review.md)说明这次rebase只保留一个测试fixture冲突；20个当前C#文件与Build.ps1共21文件等待父批独立接受后带备份应用。没有游戏启动、安装、UI、账号、永久环境、Git或旧ZIP改动。

| Evidence | 观察与哈希 | Finding / Path |
|---|---|---|
| E-01 [冻结输入及95步](validation/foundation-current-integration-20261114/run/result.json) | SHA 29ff27d9d28bb711b104b0856734d99d60b64fa9fb96cb85c694efaa76c11deb；完整1171与main原图各自稳定 | F-01（n/a_re/design/validated/high）：新组合保留metadata历史修正，未带回旧fixture；P-01 |
| E-02 [原监督终态](validation/foundation-current-integration-20261114/supervision/controller-exit.json) | SHA b4739c85cc553f2dfd61d181f242022e16ed52dadff27b65f9efafe99a9f5c4d；原supervisor34024对controller28536 Popen.wait实际0 | F-02（n/a_re/other/validated/high）：这95步属于同一图的新实际执行，launch0不充验证0；P-02 |
| E-03 [自包含归档](validation/foundation-current-integration-20261114/validation-summary.json) | SHA e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61；源码、产物/依赖、fixture、父源图差异、原中断记录均有清单 | 仅归档内字节校验成功；不把历史来源说明当作本图重跑结果 |

P-01（callflow）：[prepare_candidate](../work/foundation-current-integration-next-20261114/prepare_candidate.py)以已接受描述符图为基准→精确保留父main-r4新测试fixture→冻结1171输入及Build脚本→实际six/solution/parser→Hold62、producer11/12/8、27套普通组件（E-01/F-01）。实现位置含[Backend](validation/foundation-current-integration-20261114/source/src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs)、[Service](validation/foundation-current-integration-20261114/source/src/RizToolbox.Adapter/Gameplay/GameplayService.cs)及保留的[测试历史夹具](validation/foundation-current-integration-20261114/source/tests/RizToolbox.Judgements.Tests/LineRingSource/FakeNative.LineProducerControl.cs)。

P-02（callflow）：监督进程持有原控制器句柄→95步自然退出→原Popen.wait记录实际0（E-02/F-02）→归档→只从归档读取的checker确认全部清单（E-03）。父批main-r4的完整101/6/101 renderer及全72步仍需自己的独立终态与接受；不由本图代验。

验证：六Release W0/E0；solution0E、55条既有测试warnings；Build脚本parser0。普通组件2524项包含Gameplay175、SourceGeneration14、Runtime375。新增Build循环注册既有owner15/source10/alive6/discovery14，保留原producer、renderer及legacy循环。完整SDK矩阵未执行。

旧run-r1停在Runtime：前72自然步骤成功，原工具session79606失去句柄，controller17632和unfinished child41456均不存在，退出码及确切原因未知；该图未完成，未计PASS。完整相同源码与95步在新的受监督run-r2重跑；旧日志和缺失进程记录保留，并未拼接旧部分结果。新候选的底层两生产修正及之前失败历史见[描述符报告](2026-11-14_gameplay-descriptor-readiness-report.md)。

只读复核在项目根运行，不访问原D盘路径：

```powershell
python docs/validation/foundation-current-integration-20261114/controls/publish_evidence_r2.py --check-only docs/validation/foundation-current-integration-20261114
```

下一步必须等待父main-r4真正接受后，执行`prepare_main_payload_r2.py`、`apply_main_payload.py`（完整原字节备份与新增文件索引）及fresh main验证；这些不由候选PASS自动替代。FullSeek/HoldIdle/Startup/FullCoverage保持false。
