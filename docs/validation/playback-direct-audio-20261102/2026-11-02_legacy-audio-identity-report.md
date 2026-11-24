# 旧 AudioSource 五入口的 receiver 隔离

2026-11-02。本增量仍在 `D:/riz-build-cache-next/playback-legacy-audio-isolation-r8-20261102/`，尚未合主。它以直接六入口当前873输入为基线，不改变已合主构造器批的验收范围。未安装或启动游戏、未操作UI或账号，生产Speed/Seek关闭。

## 实际问题

`repro-r1` 使用固定旧n5 Source helper、独立编译的自有native fixture，实际调用 SetPitch、PlayScheduled、SetClip、PauseLeaf、SetTime。相同class但不同地址的AudioSource也会进入本owner的journal并推进fence；worker调用进一步使本owner Unknown。active-watch和尚无watch的两个foreign场景均真实exit1，原函数已执行，失败落在“本owner不应被污染”的断言。原stdout、命令、PID、exit和源码/产物hash保留，不改写旧结果。

## 修复

当前带watch的profile在同一native锁内按active或pending七根watch匹配精确AudioSource，再获取attempt/inflight/receipt和推进fence。其他音源不解引用、不发行managed观察或journal，原跳板按原始Single/Double/reference/void参数透传。

真实Start首次捕获还没有七根watch。该阶段仅匹配实际Start parent记录的initializer.source；保留AL1/AL0之间该source写入的fence，并让真实SetClip子调用继续接受原有parent、MI、CALLsite和单child校验。该匹配不创建watch、完成票或生产权限。其余尚未被观察的host-only对象及未知lower路径仍不是完整覆盖。

current worker在原store前使native Unknown，抑制owner-only managed观察；实际退出后不把Unknown责任当成已知可回收。SourceV4的168B frame／136B info及WatchV2的120B保持不变。

## 已有实际证据

| 范围 | 结果 | 原件 |
|---|---|---|
| 修复前foreign／early-foreign | 两进程实际exit1，counterexample成立 | `repro-r1/test-result.json` |
| 新native helper编译 | `/W4 /WX /EHa /fp:strict /guard:cf`，actual0 | `n1/native-build-result.json` |
| 五入口current、pending、worker、class、SEH及foreign／early-foreign | 27进程7767，全actual0 | `native-checks-r1/test-result.json` |
| 新helper下六direct及构造器watch／first-capture回归 | 94进程，全actual0；包含真实first SetClip child | `direct-watch-regression-r2/test-result.json` |
| 新helper下旧八role实际Dobby回归 | 14进程36675，全actual0；该scope没有新watch配置 | `original-eight-r1/test-result.json` |

以上相对原件均在本D目录。新Source SHA256为 `9bad179177053b2e1fd8db4b5bb80ec277b7d6e21d82ba2e7cc8c9514450c523`，Single为 `08b40013c6cdf9f0127c056263ad7b7452ffc38dcaa156a510be4b346da781fb`。不使用旧n5的SDK通过项验收n1。

## 仍需完成

16:30 更新：新n1的 `foundation-r1` 已实际完成28套2598、solution0 warning／0 error、独立双插件SDK14、六Release0；`mapped-r1` 实际metadata69＋19成员joint39＝108，零游戏方法／DllMain／启动。`managed-checks-r1` 十进程177、`managed-checks-r2` 五leaf Before-store75、`symbols-r2` 5PE/PDB／507文档及runner-r2两进程40已通过。`completed-scopes-r1` 只封此前已闭合范围，不代表正式组合完成。

`withdrawal-contract-r3` 实际exit0／19证明通用SDK Acquire可成功，但原生descriptor已清、SetSpeed被NotReady拒绝、没有pitch写入、旧Clock/provider关闭并最终零根。M/n5首次测试原exit1保留，错误的“通用Acquire必失败”断言改为更直接核原生控制不可恢复；产品未改。r4仅测试重载歧义build1保留，r5显式请求fresh build0/0，正式first仍须实际终态。旧长测进程缺席、无exit的现场不计成功；新helper的first、same/cross及SDK foreign/worker和12项矩阵在独立r9目录续跑。

已将三路r7仅测试增量合到本候选，新增五legacy leaf实际19成员组检查，并将五leaf的同class foreign／MI／worker flood加入正式SDK正向。新候选的fresh build、native/Runtime/first-hold、current leaf和正式SDK组合须分别获得实际终态，再精确合主。n5原scope的长测续缺项保留为独立范围，运行状态须按实时PID/command核对。

本增量不等于完整source generation、未知inline/icall/Playable覆盖、生产首次准入、任意Seek重建、effect完成或B01–B19全完成。
