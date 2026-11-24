# Seek 时间写入器独占与恢复

本批修复准备和执行入口的实际重入缺口：暂停基线 getter 可在外层尚未登记 writer 时再准备 time 或 pitch writer，随后外层覆盖登记；已有 time writer 又在设置 busy 前进行完整 graph getter，回调能写入、恢复或释放同一个 writer。旧源码的实际执行失败保存在 `reproduce-r3/`。此外，已完成 native SetTime 后的旧 API receipt 会核对旧 scheduling fence 并误撤唯一恢复来源；该原失败保存在 `reproduce-r4/`。

准备现在先保留原源 operation，并在第一个 getter 前占唯一 value writer 槽。pitch 准备共用此槽，拒绝时不做嵌套 API 读取。TimeWrite 在任何 getter 前核 registered self/thread、进入 busy，执行和恢复都保留该状态直到真实调用退出。pending time readback 的旧 receipt 返回 `NotReady`，不消费旧 native fence、不撤唯一 continuation。每次原生写入仍由实际 wrapper/lower original 和原 reservation 完成，后继身份由成功的独立读回发行。

恢复前独立读取除未确认媒体位置以外的暂停基线，包括 pitch、clip、progress/chart、offset、nullable origin 和 DSP；progress 等已变时禁止 inverse store。实际原生 continuation 仍可在媒体 getter 临时失败后执行一次原位置恢复，只有新完整读回才接受成功。永久读取失败、外部同值 writer 或已关闭来源不能因此变成确认恢复。参与者正常推进后允许独立 read 观察新 progress，但新的 media store 必须从原 non-media baseline 开始。

当前检查使用固定 SHA 的 owned 两映像 lower-time 夹具、真实 Dobby 挂接和实际 IL2CPP API shim。夹具确有 **22 source roles、23 个 physical Prepare/Commit**，但没有正式 stock issuer/foundation/Fresh ticket；本批不签正式 Fresh、正式 Seek provider 或完整 Seek。新十个场景只核实际 TimeWrite 操作，原有 Gameplay 和三个 rate/publication 场景签各自范围。

| 当前 `build-and-checks-r3` | 检查数 | 结果 |
| --- | ---: | --- |
| 准备 time/pitch 重入、写入重入、恢复重入 | 47 | 拒绝嵌套 native/API 消费，外层可正常写回 |
| 临时读失败、progress 已变、永久读失败 | 54 | 原 continuation 保留；按实际状态恢复或拒绝 |
| 外部同值 time、getter 内 close、正常正向、写前 progress 改变 | 60 | 连续 fence 保持，旧 writer 不复活，实际根最后释放 |
| Gameplay 托管回归 | 238 | 通过 |
| 正常 rate、pitch 临时读取恢复、publication in-flight | 277 | 通过；物理 Unknown 继续留根 |
| **合计** | **676** | 仅统计当前 r3 源码/载荷 |

[当前完整结果](../work/n2-seek-time-writer-20261119/build-and-checks-r3/results.json)的 **16 个子进程及外层运行器自然退出 0**。solution 与独立 stock provider 最终 Release 增量构建均 0 errors/0 warnings；首轮变更后的 solution 编译仍有 55 条既有测试警告，没有消除。1793 项当前输入、10 个 DLL 及固定 native 材料前后身份一致；六份真实改前副本、八项源码差异在[封存汇总](../work/n2-seek-time-writer-20261119/validation-r3.json)核验。用户 README 字节保持。原生材料只复用字节并在本次实际执行，不借历史 PASS 验新源。

失败原件全部保留：reproduce-r1 为测试 RootHandle 字段引用编译错误，r2 为 PowerShell `-File` 把逗号模式列表当成一个未知模式，均不是产品复现。实际产品原失败为 r3 的三个重入和 r4 的 pending receipt。focused-r1 四个核心路径通过；完整 r1/r2 与 release probes 的失败来自注入过早以及错误要求 getter 内根总数必须等于七。最终核同七个源 handle 保持，同时允许 NativeRuntime 调用两个临时根；getter 返回后七根、writer 释放后零根。没有放宽产品检查来让测试通过。

封存脚本两次换行语法失败保留工具记录，修正后自然退出0，未重跑已通过主体。

本轮没有 commit/push、新发行包、安装、启动游戏或 UI/账号操作。代理偏好为 6.1 Sol/max；现有代理未提供有效交付，本批由 root 串行实施与核验。

下一离线工作收拢正式 Seek source/publication 的运行身份边界及完整参与者通路。它们仍使用旧 formal rate/full group 检查；本批只修实际 TimeWrite，不将其升级为正式 provider 已验。固定游戏 consumer 和正式 Fresh 正向仍缺实际执行材料，R06/R07/R08 实机、真实 Speed/Seek、B01–B19 与 production 保持未完成。R08 旧预览不能验本批新源码。
