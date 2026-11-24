# 实现与有限交叉检查

2026-10-23。用户明确允许继续使用代理后，三个现有代理恢复工作；没有创建新聊天。主代理统一接线和顺序构建，代理均不执行构建或操作游戏。

| 工作 | 实现者 | 有限复核 |
|---|---|---|
| UnityEvent ledger 与13组回归 | activation_finish | 主代理读取最终实现；独立Prepare不要求父Invoke，不制造caller；始终Pending/InlineExecutionUnobserved |
| 原生图读取与独立根backend | settings_finish | never_active_retirement_code复核读取故障屏障、publication先保根与static/open等opaque判定 |
| rooted delegate只读投影、受限ref0解析、14组回归 | never_active_retirement_code | 主代理统一运行，首轮Runtime153/153通过；无listener主动调用 |
| Source借根、coordinator与销毁前后idle接线 | 主代理 | activation_finish逐处只读复核same launch/session、半初始化与隔离注册；未发现接线放行问题 |

首轮Audio548/548通过后，交叉检查发现旧Ready清理链的连续故障隔离缺口：source、initial、terminal、active backend只检查自己的局部故障，另一owner在原生读取内把同launch置Unknown后，仍可能继续后续读取、释放，active路径甚至可能继续请求Destroy。主代理与三代理分别补四条路径，统一纳入本批，而不是把首轮通过当作冻结结果。

Source修正由activation_finish只读核对：纯launch fault纳入来源Unknown；每次Target/metadata/原生读取前后核；publication返回引用先写自己的根槽，再核故障，后续才允许Target/Class；参数正常消耗确认后再检查late fault。新增WorkerDuringRefresh通过真实broker/probe的worker调用注入，拒绝故障后的新快照并保持旧图/责任。该用例只证明受控Refresh返回后的屏障，不宣称生产每字段故障注入。

Terminal由activation_finish补纯authority Unknown与逐次读取/独立free屏障；initial由settings_finish补同类屏障；active由never_active_retirement_code补读取与Destroy前后屏障。局部某根free失败仍允许其他已知独立根各尝试一次；外部launch/source故障停止后续原生操作。新增测试只覆盖其明确受控阶段，生产backend故障注入和游戏验收仍单独缺失。

这不是第二个独立runner或完整安全审计。实际统一构建结果和冻结输入以本批最终README、build-summary与归档核对为准。所有未知listener、动态转交、取消和whole-run结束边界继续保留。
