# Seek 来源与原生 Clock 接线

本批接通 Seek 共用的 source/writer/Clock 路径，并修复实际 Router 回调把自己的原生 SetTime 私有请求判为外部修改的问题。返回给参与者的时间 writer 现在绑定唯一 source/run/group/binding/origin/adoption/route/Clock，写入后沿同一个实际 writer 更新 receipt。暂停基线 getter 内切换 route 会在原生 store 前拒绝；Clock 撤回或 route 登记替换后，返回的 writer 也不能继续写。

Router 通过当前实际 native reservation/nonce 和原回调 frame 识别私有 SetTime，随后由 producer 独立登记观察并确认真实 wrapper/lower original 的返回与退出。识别不消费观察、不凭数值或“已准备 writer”标记借权。外部同值 SetTime 仍撤旧 source/Clock，不能用旧 continuation 恢复。旧单独 Seek publication 检查已删除；Seek 使用共用的 registered source 身份与实际 native Clock policy 通路。正式捕获仍要求注册的正式 provider、Fresh adoption 或真实 typed successor。

provider 的运行 receipt 校验新增 time continuation 分支。native SetTime 已成功而 API 临时失败时，provider policy 只核真实 continuation/source/scheduling fences 和 Clock，不读旧 API receipt、不撤唯一 inverse owner。恢复仍必须由同一个 writer 完成原位置 store 和独立完整读回。绑定来源的检查进入原 TimeWrite 操作，在真实 graph getter 后、native store 前以及原生恢复期间持续执行；pending successor 只能来自这个实际 writer。

Seek 准入一次检查原 pitch 通道、SetTime/lower-time 两个实际成员、GetTime in-flight 和 native fresh gate。缺 lower-time 时拒绝该操作，不误故障已有物理安装。准备 read responsibility 与 writer operation 共用释放责任；getter 内 busy Dispose 拒绝后，原根和独立准备根保持，owner 可以在真实调用返回后正常清理。准备 getter 内 source 已被释放时，返回前重新核 registered source，不遗留未交给参与者的 writer/operation；登记后的最终验证失败也清理该实际准备 owner。既有 HoldEpoch→Audio→Progress→GameState→Clock 事务及逆序恢复未重写。

| 本批实际执行 | 结果 |
| --- | --- |
| 八个 private Seek source 模式 | 135 项：native media 写入、后续 progress/chart 观察、原生 Clock 发布/反向恢复、临时读失败恢复、失权拒绝、外部同值失权、busy 根保留及准备中释放 |
| 十个 actual TimeWrite 相邻模式 | 161 项，使用本批新受测 DLL 重跑 |
| Gameplay 托管回归 | 238/238 |
| 正常 rate、pitch 临时读回恢复、publication in-flight | 277 项，使用本批新受测 DLL 重跑 |
| **合计** | **811 项通过，24 个子进程及 outer 自然退出 0** |

最终 Release solution 增量构建 0 errors/0 warnings、独立 stock-provider Release 0 errors/0 warnings；本批首次完整改后编译仍有 55 条既有测试警告，并未消除。1793 项当前输入、10 个 DLL 和七份 native 材料前后身份一致；用户 README SHA256 保持。最终结果在[本批 r2 运行记录](../work/n2-seek-source-operation-20261119/build-and-checks-r2/results.json)，[r2 封存汇总](../work/n2-seek-source-operation-20261119/validation-r2.json)核对 10 项修改、1 项新增、1 项删除及 11 份真实改前副本。封存工具自然退出 0，没有重跑已过主体。r1 的 795 项是准备释放修复前的阶段结果，只作历史；不能验最终源。真实准备阶段改前副本保存在 `source-before-preparation-fix/`。

原失败保留：`reproduce-r1` 的 withdrawn Clock 和 `reproduce-r2` 的 route replacement 均证明返回 raw writer 仍执行了 native store；`reproduce-r2` 的 readback 模式先被 Router 私有 SetTime 误撤阻塞，不能冒称已复现 provider pending 缺口。只修 native reservation 路由后，`reproduce-provider-r1` 在独立 post-store getter 失败后确实复现 provider 误撤。`reproduce-gate-r1` 证明仅入口 wrapper guard 不足，读取中换 route 仍执行 store；本批增加同源 writer 内部检查后通过。`reproduce-prepare-r1` 实际准备 getter 内释放 source 后，原 writer 仍被登记而未返回，child exit1；补返回前核对及失败准备 cleanup 后 `focused-preparation-r1` 三模式55项通过，最终重跑 r2 全部相关检查811项。各 child 原 exit 1/log 保持，预期失败运行器的 exit 0 只说明捕获成功。最初 work runner CRLF 模式块替换失败未执行任何构建，其工具原错误保持。

测试使用固定两映像 owned 22 source roles/23 physical members 和真实 Dobby/IL2CPP API shim。private SeekSource 通过反射直接构造以执行共用内部机制；没有设置正式 provider/host/adoption，历史 fixture provider 的正式 CaptureSeek 持续拒绝。正常路径的 progress/chart 阶段由 API shim 改真实输入，不是 `NativeSeekProgressParticipant` 或完整 Hold/判定/renderer 事务的实际执行。**不签正式 Fresh/正式 Seek provider/固定游戏 consumer/完整 Seek 或生产准入**。

没有 commit/push、新包、安装、启动游戏或 UI/账号操作。两名新代理显式用 gpt-6.1-sol/max，仍只返回待命询问，没有有效交付；本批由 root 串行实现与核验。

下一离线工作沿既有完整参与者补 `NativeSeekProgressParticipant`、gameState/HoldEpoch 和 Clock 的实际组合执行材料。先补实际进度缓存与转换 profile 的可执行边界，再走原事务的提交和逆序恢复；不得把本批注入 progress 阶段签作完整参与者通过。正式 Fresh/fixed consumer 正向、R06/R07/R08 实机、真实 Speed/Seek、B01–B19 和 production 仍未完成。相机/N1/GetTime consumer/pitch恢复/owner同步撤权/退休批及 R08 旧包矩阵不重做。
