# 同次组合校验去重

本批处理真实游戏恢复播放后仍有分钟级卡顿的问题：普通 hook 校验在一次映像组合中重复执行，且每个 hook 都重读同一 Dobby 完整映像。修改限于 Runtime 两个文件；新构建、210 项 hook 检查、338 项当前播放链回归及48项普通/组合检查全部通过，共596项。实测接管仍需127.13秒，未解决可用性问题，测试游戏已结束。

本地已授权游戏兼容层实现；普通工程报告，flavor=null。未改 SDK 公共合同、Adapter 或 native helper；没有账号、外部通信或生产准入操作。

| ID | 原件 | 范围 |
|---|---|---|
| E01 | [上一包实际结果](../work/game-seek-20261117/scheduled-engine-child/scheduled-r1-game-result.json)与[主线程栈](../work/game-seek-20261115/scheduled-engine-child-r1-after-resume-a-stack.log) | 恢复播放约251秒，栈在普通成员映像验证；仅定位上一包。 |
| E02 | [当前源码冻结](../work/game-seek-20261117/composition-read/source-freeze.json) | 1504项输入，仅ReferenceCodeIdentity.cs与Playback.Group.cs两文件变化。 |
| E03 | [本批构建](../work/game-seek-20261117/composition-read/managed-build-result.json) | Loader及其六组件、Examples、Hooks串行Release成功；Hooks210/210。 |
| E04 | [当前播放链回归](../work/game-seek-20261117/composition-read/checks-runner/checks-r1/result.json) | 新Runtime及其确切依赖/原生组件；Replay113、Resume133、直接时间46、直接Engine46，共338。独立owned23成员组，无stock/Unity/正式adoption/Clock。 |
| E05 | [普通成员及新组合检查](../work/game-seek-20261117/composition-read/ordinary-checks-r2.json) | 六独立进程、48项，实际Dobby晚装和真实私有快照。含新ForComposition出口分支/Dobby破坏拒绝、恢复仍sticky；与候选Runtime字节一致。未构造playback group。 |
| E06 | [新包回执](../artifacts/delivery-20261117/composition-read-preview-receipt.json) | 35成员/25运行文件全部读回，绑定E02–E05及相同字节的原生继承证据。 |
| E07 | [本包实际游戏结果](../work/game-seek-20261117/composition-read/composition-r1-game-result.json)与[本轮栈](../work/game-seek-20261115/composition-read-r1-capturing-a-stack.log) | 25安装匹配，source127.13秒；Pause实际回执前采样已暂停，前置条件19拒绝；Resume dispatch32.73秒，无随后实际Playing/Ready。正常退出请求后仍无响应，精确结束该测试进程。 |

F01（E01–E02）：实际主线程路径通过嵌套fresh accessor进入多次完整组校验，普通成员又各自扫描同一Dobby映像。代码检查确认同一engine的RetainPlaybackAuthority返回同一私有seal对象；不同engine不可据路径或相同哈希合并。

F02（E02–E05）：一次同步组合在真实Gate锁内先完整CaptureSnapshot，canonical/patch中间阶段核对固定owner/generation/member引用，最后完整Verify。共享Dobby扫描仅在每一次完整验证阶段的局部引用集合内生效；入口、出口分别新建集合。每个member的publication、registration、before页身份、installed/relay/originalCode、replacement页身份与metadata仍逐个验证。独立Verify与原生getter的fresh边界保持，不存在跨回调或时间缓存。

F03（E05）：夹具新composition首次运行已拒绝实际分支损坏，但最后把普通Bind的私有playback Prepare/Commit计数错误预期为2/2；实际应为0/0。原失败结果及源码已保留。仅修夹具这一断言后重编并重跑两个新mode，未重复已通过的四旧mode，产品源码未变。r2回执分别绑定四旧结果和两个新结果，以及两份runner的同一候选Runtime。此检查直接调用真实ForComposition方法，但不代表已构造同时含晚装ordinary member的playback group。

P01（E02–E05支持F02–F03）：`Gate → CaptureSnapshot/full member entry → canonical preimage与重叠检查 → 各实际owner贡献 → ApplyPatches → full member exit → 返回完整组合结果`。损坏必须在结果返回前拒绝；恢复内存不能解除unknown。

新Runtime：1715200字节，SHA-256 `89313403a5d04cd6b840ee7479db870fe6b9089dc75ace83200a8dd01bb062c5`。

新ZIP：37315227字节，SHA-256 `f30bfac4c07a79b54adbc3e54497e8b9d28e320bed1d8d05eb45aa9c880442cc`。原native helper未重建；其442项结果仅按相同helper和未变native源码继承，不计入本批596项。

F04（E07）：这两文件不足以解决卡顿。实际栈位于StockTargets.Inspect，经Lower/Engine supporting/pair回到Targets.VerifyCurrent的traversal出口，fresh accessor仍多次进入完整验证；共同Dobby和重复祖先仍有后续工作。正常AltF4已发送，观察后等待140.68秒仍无响应，12:25:18核PID/路径/启动时间后精确结束11456；不记正常退出通过。

复现命令与每步自然退出码见E03–E05，均在新的结果目录串行执行。后续先解决上述剩余阻塞，再验SDK Pause、真正Clock Ready及Speed/Seek；B01–B19和production仍未完成。
