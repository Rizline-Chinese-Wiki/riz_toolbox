# I 批时间线

日期均为 2026-10-26；以下按观察先后排列。未从原件取得准确墙钟时刻的事件只记顺序，不编造时间。E/F/P 编号对应[正式报告](../../2026-10-26_底层-生产启动组件接线-report.md)。

| 顺序 | 已观察事件 | 证据与边界 |
|---|---|---|
| 1 | 早期 candidate r1 因 `_local_unwind` 链接失败，r2/r3 因严格编译问题失败；原件均保留 | 历史失败，不计通过 |
| 2 | r4 实际完成候选构建与静态检查；托管生成文件仍有 CS8669 | 后续 r5 重建，没有回写 r4 原件 |
| 3 | 最终核心 r5 根 session 65289 退出 0，托管六组件 0 警告/错误；2755 条离线检查/17 条磁盘边/82 导出通过 | E-I01/E-I02；Doorstop 上游警告另存，磁盘核验不是生产 admission |
| 4 | r5 冻结 665 项目源及 2 生成文件；带最终 profile 的 11 项指纹/gate 检查与 7 项 V4/6 项协调器模型通过 | E-I03/E-I04/E-I05；受控模型不是游戏结果 |
| 5 | Audio/content r2 实际 640/640；r1 缺环境输入的失败保留 | E-I06 |
| 6 | 最终候选包 r2 形成 24 payload、10 evidence；安装 r4 最终完成 13 场景、98 断言 | E-I07/E-I08；合成游戏目录，不加载 native/game |
| 7 | 安装 r4 的 worker PID 13012 在实际移除旧文件后终止，退出 -1；独立恢复 PID 36652 退出 0，恢复 27 文件，pending=0 | E-I08；不是仅模拟异常 |
| 8 | owned/default 兼容构建及两个 owned 普通/异常进程完成 | E-I09/E-I10；没有重跑完整故障矩阵 |
| 9 | cold r1 实际 Host PID 44716 退出 0，但离线验证错误地固定 early Close(NULL) 错误码 6，实际观察 5023，验收失败 | 原始 case/bin/失败日志保留；不计通过 |
| 10 | cold v2 增加同阶段系统原函数独立对照；fresh r2 根 session 40857 构建退出 0 | E-I11；核心候选使用 r5，无新 public exports 或 Bootstrap ABI 观测调用 |
| 11 | cold normal PID 38856、20688 各退出 0，每例 204 条离线检查；三阶段均 Disabled/state5/failure5、infoReads3/Verify0、无写入职责 | E-I11/E-I12；自有 Host 不含游戏 Unity 链，不证明固定游戏 17 条运行时边 |
| 12 | 缺 Bootstrap PID 43176、缺 Kernel PID 44308 均退出 -1073741515，未出现 main 原件；cold r2 四例汇总 passed=true、所有持有输入哈希不变 | E-I11；加载器依赖拒绝，不宣称缺依赖进程运行了 Bootstrap |
| 13 | cold v2 的 9 行声明源码保存为独立 supplement，叠加 r5 核心 ZIP 内旧 cold v1；生产组件未重编 | E-I11；supplement ZIP SHA256 `d522f58dd4040d86badf202e3a60099d9f4251a2c71ec018bbc3c25721d07df6` |
| 14 | J 批双 receiver 生命周期新构建结束，完整 Audio/content 660/660 与 Runtime owner 10/10 受控检查通过（含 23 项新增），独立证据索引见 C:/Users/71957/riz-build-cache/engine-receiver-lifetime-r1-20261026/evidence-index.json | 进行中，不计入 I 批通过数量；真实游戏实测仍稍后 |

本阶段关闭的是候选组件、合成安装事务及自有冷启动证据缺口。生产游戏 admission、完整销毁/取消/资源退休和 B01–B19 总验收保持未完成。范围见[scope.md](scope.md)。

