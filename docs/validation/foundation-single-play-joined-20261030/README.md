# Single native delivery 与 Play 效果账本联合闭合

正式 `SingleDispatch` 与本地联合 fixture 现在使用同一个 live `Bridge.Delivery` 读取入口。实际 native Single producer 的 Mid/Before/Returned 经 native TLS `ReadCurrent`、真实 broker physical scope、正式 executing-list/dispatch reader 和生产 Play consumer reader，到达原有私有 transfer 与 occurrence 完成账本。

fresh 固定基底的 **7/7 独立进程**全部 exit 0；native `/W4 /WX` DLL 构建通过，managed 构建 0 error、26 个既有 fixture warning。两个相同 Tween 指针的队列 occurrence 在第一次退出后仍等待，第二次退出后才可借完成区间。未知 listener 和缺失 post 始终保留 Pending。完整结果、构建日志、输入与 hash 见 [证据索引](evidence-index.json)。

本包实际执行的是自有 native DLL 与生成的自有 SEC_IMAGE 跳转代码。Play operation capture、source journal、admission 和 death 继续使用受控 completion fixture；没有验证真实游戏对象或生产 FirstInstall，也没有安装/启动游戏、操作账号或 GUI。此前 39 项效果专项、65 项订阅托管检查及 42 模式 native 基底没有重跑，不能把它们算成本包新增执行结果。

实现与证据边界见 [专项报告](2026-10-30_implementation-single-play-joined-report.md)，另附 [范围](scope.md)、[过程](timeline.md)、[完整 owned 输入](overlay-inputs.json) 和 [最终结果](test-result.json)。

```powershell
C:/Python314/python.exe docs/validation/foundation-single-play-joined-20261030/run-checks.py --output D:/riz-build-cache-next/single-play-joined-replay-20261030
```

输出目录必须不存在。脚本只复制上一包固定基底并覆盖这次 owned 输入，不编译其它 owner 的 live 改动。相机正式整合与全树结果由 root 独立验收；本包不修改相机。
