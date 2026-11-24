# Gameplay 描述符就绪：离线修正与验证

本候选修复了“新捕获的音频描述符尚未就绪却被直接判为过期”，以及可用性切换后旧独占控制租约未结束的问题。独立 DSP、宿主时钟和已保根游戏字段继续读取；写入仍须持有当前精确描述符。完整1171输入、45项目解决方案及本批90个自然步骤通过，其中27套普通组件共2524项、Hold相关62项分别记录。候选尚未合主；完整SDK、B01–B19、真实Speed/Seek及父批完整renderer均未由本候选验收。

## 范围与约定

范围来自用户继续N2离线功能工作的授权及[项目合同](../.docs/foundation-expansion/SPEC.md)。本批只改Backend、Service和SourceGeneration测试入口三个文件；[源码审查](../work/gameplay-descriptor-readiness-next-20261114/source-review.md)说明边界。本文采用通用技术报告结构，flavor=null；没有独立安全评估case，IOC与ATT&CK不适用。没有安装/启动游戏、操作游戏UI、账号、永久环境、Git或旧ZIP。

## 证据、发现与路径

| Evidence | source_ref / content_hash | 支持的Finding | 实施位置与Path |
|---|---|---|---|
| E-01 冻结实现与测试 | [完整输入与90步结果](validation/gameplay-descriptor-readiness-20261114/run/result.json)，SHA 72c3e986142f2b43947936a06b3e05c4e23d2eb101d5c3cfc38147f2a59ce495 | F-01：fresh null/null应NotReady，曾有或已变描述符仍TargetExpired。n/a_re / design / validated / high | [Backend.VerifyCapture](validation/gameplay-descriptor-readiness-20261114/source/src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs)，P-01 |
| E-02 实际provider/根/租约测试 | [14项直接测试日志](validation/gameplay-descriptor-readiness-20261114/run/suite-GameplaySourceGeneration.log)，SHA 96f2bfe5b6d586216b88ca7ab35d047901d9f21a855e7d35402bc5eb6db8edbe | F-02：descriptor可用性切换应结束独占控制owner，不能让旧租约借到后出现的写权限；独立观察不因此失效。n/a_re / design / validated / high | [Service.Capture](validation/gameplay-descriptor-readiness-20261114/source/src/RizToolbox.Adapter/Gameplay/GameplayService.cs)，P-01 |
| E-03 原工具实际退出 | [root终态](validation/gameplay-descriptor-readiness-20261114/controls/run-r3-root-exit.json)，SHA cdcb9dbdf32196392242edcef2d274cab7d5cba087218d3b204205b34c5823d1 | F-03：全部90步及完整输入在本轮实际完成；未拼接旧部分结果。n/a_re / other / validated / high | [候选验收](../work/gameplay-descriptor-readiness-next-20261114/candidate-acceptance.json)，P-02 |
| E-04 自包含归档 | [文件清单](validation/gameplay-descriptor-readiness-20261114/validation-summary.json)，SHA 7c1d950ddbfa491ea3316de872a9fa68e2a40c03a4b1379cd55c3cb011717492 | 源码、实际产物/依赖、fixture、失败图差异和日志可只读复核；1987文件/221618253字节 | [只读检查器](validation/gameplay-descriptor-readiness-20261114/controls/publish_candidate.py)，P-02 |

P-01（callflow）：Controls.Acquire捕获并绑定实际Service代次/运行→每次Capture发现descriptor availability切换时结束旧owner（E-01/F-02）→Execute对未就绪的新捕获在provider前返回NotReady，对丢失旧authority返回TargetExpired（E-01/F-01）→实际provider准备期间再次验证，恢复后新租约才允许音频/Clock确认（E-02）。

P-02（callflow）：完整1171冻结图→六十二项Hold与27套普通组件直接进程→原root实际exit0（E-03）→冻结1987文件与历史差异→仅归档内字节校验（E-04）。本图的旧metadata历史fixture仍须显式rebase到父批新的测试修正；不以此替代新图验证或父renderer验证。

## 验证与失败历史

本轮run-r3 controller39064/root78272实际0/85002b。solution45项目0 errors、55条既有测试warnings；Hold62全过；普通组件27套2524/2524，其中Gameplay175、SourceGeneration14、Runtime375均为实际新进程结果。归档root80909实际0/3cfc66；直接归档检查root8614实际0/74fd71。fullManagedMatrix=false：完整Judgements和父renderer不在本轮范围。

历史逐轮保留：run-r1发现还需结束旧owner；run-r2及focused-r3发现新增测试错误地重用已失败租约；focused-r4通过13/14后遇到旧optional-descriptor异常断言。最后一项现检查独立DSP/host读取、重复捕获根数量稳定、旧owner失效、零provider/写入/audit、恢复后确认及Dispose根归零。未把可选descriptor失败强改成独立字段失效；正常Gameplay175仍检验实际原生捕获失败的清理。focused-r5的14/14只是定向证据，当前全90步另行新跑。

## 只读复核

在项目根目录运行；此命令仅访问该归档内的文件，不要求访问原D盘运行目录：

```powershell
python docs/validation/gameplay-descriptor-readiness-20261114/controls/publish_candidate.py --check-only docs/validation/gameplay-descriptor-readiness-20261114
```

它核对完整1171输入、直接输出/依赖、native owned fixtures、原root退出、每步日志及历史源图的可重建性。新运行需要原工程的.NET构建环境；归档校验不构成再次执行功能测试。下一实施位置为[当前合并候选](../work/foundation-current-integration-next-20261114/source-review.md)：保留父批metadata历史修正与Hosting输出修正后新验，再等父main-r4实际终态接受后合主。
