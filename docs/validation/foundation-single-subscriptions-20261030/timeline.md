# 时间线

1. 读取上一批真实源码、42 项 Single 测试和 2026-10-29 固定证据；确认新 Subscriptions/Scope 已落盘，但没有对应回调测试。
2. 初始 live 定向编译成功，生产 Runtime/Adapter 0 警告，测试共享夹具 26 条 CS0649；结果位于 D:/riz-build-cache-next/single-subscription-diagnostic-r1-20261030。
3. 补 22 项实际委托/Scope 检查，含回调挂起与恢复、退出、已选 peer、fault、一次清理、两 run 和 GC。r2 诊断 64/64 通过。
4. 补固定 helper 的真实 native operation → managed broker 回调测试。r3 live 编译碰到并发 GameAppearanceService 未完成接线的 5 错，未改非 owned 源码。按 root 指令停止编译 live 全树，改用精确固定 V7 core 基底加 owned 覆盖。
5. 2026-10-30T02:15:04Z 启动 fresh 隔离冻结：D:/riz-build-cache-next/single-subscriptions-frozen-diagnostic-r1-20261030。复制 622 基底输入，仅覆盖本包 17 个完整 owned 文件及 Retirement 两方法，最终 626 输入。
6. 同一冻结实际通过 64/64 Single、8/8 native callback 子进程和 17/17 旧 typed/completion 定向回归；两个构建 exit 0，基底/helper/冻结源码/owned 工作区前后哈希一致。保留这份成功诊断，再完成下面的新边界复核。
7. 复核补上 Active 但 Unknown 订阅拒绝新借用的 hub 守卫和第23项新实际回调反例；同时加强运行结果 contract 核验，并保留 Retirement 非 owned 区域的原始换行字节。最终 fresh 输出 D:/riz-build-cache-next/single-subscriptions-frozen-r1-20261030 实际65/65 + native8/8 + 旧定向17/17通过，所有前后哈希不变。
8. 生成专项报告、源清单、日志与证据索引，交回 root 进行统一全树验证。本包不使用旧 full25/2233 或 release/adoption 结果替代本次改动后的整体验收。
