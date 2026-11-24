# R06 原生故障同步撤权证据

当前产品从真实 retained source/GetTime poll 同步取得 sticky fault reason，在当次 owner maintenance 撤回旧 Clock 和 SDK 原生控制权限。外部通知继续异步投递并保留 exact fault 对象；正常逻辑 withdrawal/policy inactive 不判物理 Unknown。

- [实现与验证报告](../../2026-11-19_owner-synchronous-fault-report.md)
- [最终汇总：1791 输入、1003 项、12 个自然退出子进程](../../../work/r06-owner-fault-poll-20261119/validation-r3.json)
- [当前构建及检查原结果](../../../work/r06-owner-fault-poll-20261119/build-and-checks-r3/results.json)
- [外层运行器自然退出 0](../../../work/r06-owner-fault-poll-20261119/build-and-checks-r3-exit.json)
- [产品改前稳定复现，owner/host 均退出 1](../../../work/r06-owner-fault-poll-20261119/reproduce-r1/reproduction.json)
- [独立封存运行器自然退出 0](../../../work/r06-owner-fault-poll-20261119/seal-r2-exit.json)

两个新场景使用真实十三成员 owned constructor group，实际 worker before-store Unknown、SuppressObserver、原调用一次、physical exit 一次，owner 首次维护即 Withdrawn，通知恢复后不复活、七根保留。宿主用 test-only reflection 接同 group/route/session，仅签维护/节流行为；不签正式 Fresh 安装或 22-role scope。

正常 Unity backing 切源、真实 Start/Pause 后 SDK 恢复变速、零根清理也在本次新字节上执行。旧独占双 writer 和 controller generation 断言的修正仅发生在测试；r1/r2/focused 原失败及 seal-r1 语法失败保持。最终两个 Release 增量 0E/0W 不表示本轮首编译的 55 条既有警告已消除。

无游戏、UI、账号、安装、commit/push 或新包。正式 Fresh/fixed game consumer、真实 Speed/Seek、R06/R07/R08 实机、B01–B19、production 未完成。封存旧 R08 包不包含本次源码。
