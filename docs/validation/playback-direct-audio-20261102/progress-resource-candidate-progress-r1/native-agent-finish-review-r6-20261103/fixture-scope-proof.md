# fixture-r2 与按模式选证

helper的7个输入在live、r6 frozen、fixture-r2 frozen完全相同；functional-matrix-r1可用冻结副本逐项相同，见source-audit.json。全部选择模式实际测试的helper均为ef24415492cd31333a692ef689f2dac5c34c7ca7a58a6b771316ceb0dc5d353c。

fixture-only-correction.patch已由脚本按精确白名单核验：只改closed分支ordinary frame预期host2/nonhost4、noJournal=ordinaryFrames+1，补originals counters断言。其他模式分支未变。runner-only-correction.patch只改host closed的noJournal5→3，nonhost仍5。

| 模式范围 | 选择证据 | 说明 |
| --- | --- | --- |
| 原46 | native-agent-retry-short-r6 | 原fixture+原helper，46/46、7969checks，未重跑 |
| host-responsibility-closed | native-agent-responsibility-host-r6-fixture-r2-resume-20261103 | 新fixture-r2+原helper，217checks、actual0 |
| responsibility-closed | native-agent-responsibility-short-r6 | 原nonhost已过；ordinary4/noJournal5与修正后的nonhost预期相同 |
| host/nonhost worker、seh、class、saturation共8项 | native-agent-responsibility-short-r6 | 分支与helper未变，各自actual0 |

这是逐模式组合的56项证据，不声称10个marker都在fixture-r2重跑。新originals counters断言仅在此次host补测执行，nonhost仍引用原实际通过证据。原host失败actual1、216checks/2failures完整保留，旧manifest未改写。
