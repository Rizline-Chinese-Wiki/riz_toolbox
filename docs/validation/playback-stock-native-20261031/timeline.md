# 执行时间线

本表时间来自实际manifest，UTC对应北京时间+8。历史结果只用于其各自固定版本，未为报告重跑旧目录。

| UTC / 北京时间 | 阶段 |
|---|---|
| 2026-10-31 03:48:35–03:48:45 / 11:48:35–11:48:45 | build-r1 link exit0，ASM PUBLIC缺DLL export；不能调用target。 |
| 03:50:50–03:51:00 / 11:50:50–11:51:00 | build-r2 link exit0，仍缺ASM export。checks-r1八条已记child exit为0xc0000005；中断空日志无exit。 |
| 03:52:13–03:52:24 / 11:52:13–11:52:24 | fresh build-r3显式导出target；checks-r2于03:52:50–03:52:51完成旧10进程/20028，单独保留。 |
| 03:55:43–03:55:53 / 11:55:43–11:55:53 | build-r4增加worker withdraw、After窗口和MASM caller；五TU实际exit0。 |
| 03:56:40–03:56:42 / 11:56:40–11:56:42 | checks-r3对r4完成13进程/26920全部actual exit0；当时没有executable DIR64。 |
| r4后续文字报告 | 最初四份md只写文字；它们引用的本地evidence/index/sources未实际生成。本次将四份原文归档history/r4-reported，直接从D原件补当前证据。 |
| 07:58:12.289–07:58:22.544 / 15:58:12.289–15:58:22.544 | fresh build-r5真实生成.text DIR64且编译exit0。 |
| 07:59:13.121–07:59:14.301 / 15:59:13.121–15:59:14.301 | r5新增runner错误把mapped ImageBase作preferred，13进程/26998检查、各两条新增断言失败、exit1。独立静态verifier误只接受object.text；真实贡献为.text$mn。失败目录完整保留。 |
| 08:00:21.469–08:00:30.700 / 16:00:21.469–16:00:30.700 | fresh build-r6：只修runner读取冻结DLL磁盘头；保持helper/source协议与fixture的实际DIR64设计。五TU/link exit0，输入及冻结源前后hash未变。 |
| 08:01:11.045–08:01:12.017 / 16:01:11.045–16:01:12.017 | checks-r6，13进程/27037全部actual exit0，每进程Dobby8；每个实际非preferred映射下的.text anchor核验通过。Test脚本外层exit0。 |
| r6同次独立静态核验 | Verify-PlaybackSourcePrototype.py外层exit0，3PE/PDB、5COFF/C13、2固定target、真实.text DIR64与COFF ADDR64来源通过；静态工具不执行映射或游戏代码。 |
| r6报告阶段 | 通知root、group和profile任务最终r6路径/hash；冻结十个owned及原始日志、补全E/F/P/全输入输出hash与只读复核。root/src/native/scripts保持冻结，旧D不变。 |
