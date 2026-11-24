# I 批范围与授权

更新于 2026-10-26；普通 PE 逆向及实现验证，`flavor = null`。本文件限定 I 批阶段证据范围，不替代完整 B01–B19 目标。

| 字段 | 约束 |
|---|---|
| `auth` | 用户授权继续完善 PC Mod 公共兼容底层并使用多代理；后续明确“先继续代码，实测稍后” |
| `in_scope` | 本地项目源码、独立候选构建/打包、只读 PE/哈希核验、受控托管/原生 fixture、用户目录内合成游戏安装/升级/降级/卸载及持久 journal 恢复 |
| `out_of_scope` | 写入真实游戏目录、启动或停止真实游戏、将生成指纹或 JSON 当作生产首次写许可、将自有 Host 冷加载扩张为固定游戏加载证明 |
| `network_profile` | 本批报告使用本地文件及本地子进程证据；cold 只做 Close(NULL)、NO_PROXY Open/Close，无 HTTP 请求、PAC 或自动发现 |
| 固定游戏输入 | `steam-25977441` AMD64 `Rizline.exe`、`UnityPlayer.dll`、`GameAssembly.dll`；真实游戏目录只读 |
| 最终核心候选 | `C:/Users/71957/riz-build-cache/engine-production-r5-20261026` |
| 最终独立包 | `C:/Users/71957/riz-build-cache/engine-production-package-r2-20261026`，24 payload、10 evidence |
| 合成安装验证 | `engine-production-install-cases-r4-20261026`，13 场景/98 断言，真实安装 worker 终止后新进程恢复；不加载 native/game |
| cold 验证 | `engine-production-cold-r2-20261026` 构建及 `engine-production-cold-cases-r2-20261026` 的 4 个自有进程；两个正常、两个缺依赖拒绝 |
| 生产权限 | 源码首次写许可保持 false；`productionAdmissionProved=false`，真实游戏尚未安装本批候选或执行 |
| 未完成项 | 固定游戏运行时 IAT/首次执行窗口、双 receiver 死亡、销毁 pending、取消完成、迟到/复活处理、全部消费者与资源退休、整局/重试/返回菜单游戏验收 |

## 证据归属

r5 核心使用冻结的 665 份项目源与 2 份生成文件。cold v2 是后续独立测试增量，以其实际构建声明中的 9 行源码哈希及 `cold-fixture-sources.zip` 补充归档为准；补充归档不重编生产组件、不修改核心构建原件。J 批生命周期工作处于实现中；新构建已结束，23 项新增回归仍在运行，尚不计通过。I 批不能对随后活跃源码宣称不变。

H 批冻结进程与故障矩阵只作为历史背景，不加进 I 批通过数量。I 批失败目录及日志保留；正常子进程退出成功但独立验证失败的 cold r1 仍为失败尝试。

本批实际结果及边界见[README](README.md)、[时间线](timeline.md)与[正式报告](../../2026-10-26_底层-生产启动组件接线-report.md)。
