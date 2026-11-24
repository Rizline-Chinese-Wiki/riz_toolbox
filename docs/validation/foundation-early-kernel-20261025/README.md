# 新 Kernel 早期运行链：正式归档

2026-10-25。root实际观察session 17459 终态0；新链73、旧publisher57、旧链73均通过。此为独立原生集成批，旧25套1617项与23项包属于core23528，本批没有重跑或替换。

- [正式报告](../../2026-10-25_底层-新核运行链验证-report.md)、[最终汇总](final-summary.json)、[真实终态](terminal.json)、[七步骤](command-result.json)。
- [604源码清单](source-final-manifest.json) / [冻结ZIP](source-final-inputs.zip)，SHA `b3e20129db468c400a58ac2e806df9965da897cd56c12ad552857ba9252869be`。
- [18二进制行](binary-final-manifest.json) / [ZIP](final-binaries.zip)，SHA `34aa3bdf7afec3230c1e4338ed115e7a5b7a6c40c7e0fca8e71fec7533e0fe34`；7+4+7隔离身份。
- [完整缓存原件](final-cache-raw.zip) / [原件清单](raw-final-manifest.json)，SHA `86a0b4db885189c62d0d91c62d61abc14961434595b451a1f2e770e34eab1b51`。
- [新链73](raw/cache/kernel-cases/fixture-result.json)、[旧publisher57](raw/cache/legacy-publisher-cases/fixture-result.json)、[旧链73](raw/cache/legacy-cases/fixture-result.json)。
- [31803原失败](history/first-attempt/terminal-observation.json)、诊断原件均保留在history，不计正式通过。
- [代码接续](production-next.md)、[编译profile](native-notes.md)、[脚本静态审查](scripts-review.md)。

Publisher记录2、共享wire3、Kernel ABI2/scalar1分别核验。旧JudgeContext仍独立存在，两模块不共享Ready/Unknown。仅自有Targets，未授生产V4、游戏接入、取消或全局退休；无安装和游戏执行。
