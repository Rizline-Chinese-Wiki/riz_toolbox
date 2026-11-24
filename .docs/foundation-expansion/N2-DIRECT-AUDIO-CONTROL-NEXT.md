# N2 下一离线增量：直接 AudioSource 播放、停止和循环写入

2026-11-02 13:20施工状态：六个独立入口已接入D-only组合，尚未合主。真实U64 kind7、18source＋GetTime、source全局32槽和正交WatchV2／120已实现；native94／15556及原8role14／36675、正式19次Dobby安装后的native与NativeRuntime两路径68检查已通过，双SDK撤权及mapped19成员继续执行。固定owned fixture6258具19本体／RF／UW和8确切CALL独立profile；实际范围和下一缺项见CURRENT-CHECKPOINT顶部。下方“尚无新入口执行”是原任务建立时的状态，保留历史，不用旧2598签本增量。

2026-11-01。此任务以当前首次捕获已合主的 850 个输入为基线。共享 MUG 构造函数增量正在独立 D 目录实现，须先验收并合并，再按其最终源续接本任务。本文记录已定位的实际入口和实现要求，尚无新入口的执行验收。

## 问题与已确认事实

当前共同组覆盖 AudioSource.Pause、PlayScheduled、SetPitch、set_clip、set_time，以及 MUG/Progress 的既有来源。固定映像另有六个不同的直接入口。调用它们可以到达播放、停止或循环设置的 icall，而不经过当前已挂接的 AudioSource 方法入口。最终读数一致不能证明中间没有同值或恢复写入。

只读原件位于 `D:/riz-build-cache-next/playback-direct-audio-control-profile-r1-20261101/evidence-r1/`：`profile.json` 保存每个入口的完整 metadata 区间字节、反汇编、RF/UW、RIP 缓存和 resolver 字符串；`source-current-entry-inventory.json` 固定当前产品源。`test-result.json` 只证明静态定位成功，没有执行运行时反例，也没有证明 Unity 引擎底层覆盖。

| 入口 | RVA / 完整区间长度 | 实际参数渠道 | 调用的 icall |
|---|---|---|---|
| PlayHelper(AudioSource, UInt64) | `1511680` / 80B | RCX source、RDX UInt64、R8 MI | `PlayHelper(AudioSource,UInt64)`，cache `1fb0658` |
| Play(Double) | `1511730` / 80B | RCX self、XMM1 Double、R8 MI | `Play(Double)`，cache `1fb0660` |
| Play() | `1511780` / 64B | RCX self、RDX MI；原函数写 delay=0 | 直接使用 `1fb0658`，不调用 PlayHelper 的 IL2CPP 入口 |
| Stop() | `1511810` / 64B | RCX self、RDX MI；原函数写 DL=1 | 直接使用 `1fb0668`，不调用 Stop(Boolean) 的 IL2CPP 入口 |
| Stop(Boolean) | `1511850` / 80B | RCX self、DL raw bool、R8 MI | `Stop(Boolean)`，cache `1fb0668` |
| set_loop(Boolean) | `1511a70` / 80B | RCX self、DL raw bool、R8 MI | `set_loop(Boolean)`，cache `1fb06b8` |

PlayScheduled 与 Play(Double) 共用 `1fb0660`，但两个入口独立向缓存目标跳转。Play() 与 PlayHelper、Stop() 与 Stop(Boolean) 也同理。共享缓存不能用作只安装其中一个入口即可覆盖另一个入口的依据。缓存均在磁盘 `.data` 的零填充虚拟部分；静态文件不含运行时解析后的 Unity 实现地址。

## 实现合同

1. 在共享构造函数最终提交之后，保留现有角色数值，追加六个独立角色和确切 metadata / body / RF / UW / DIR64 证明。每个不同物理入口独立安装、验证、展开和持续封存，保留所有原来源及只读消费者。
2. 为 PlayHelper 增加真正的 UInt64 参数 ABI；不得把 RDX 的数值改送到 XMM1，也不得用 Double 数值转换保存 bits。Boolean 继续保留实际字节，覆盖 `00/01/80/ff`。原始 MI、caller、浮点位模式、非易失寄存器、异常和实际 original 次数保持可核对。
3. 这些调用只撤回当前音源的旧 Clock/source/writer/prepared 权限，不签出 Pause、schedule、初始化完成、新代次或恢复凭据。相同状态、参数为空、错误 MI、异常和 A→B→A attempt 均须在原调用可改变状态之前结束旧权限。
4. 用精确已保留 AudioSource 的私有 watch 在 native 中决定相关性。其他音效 source 的调用在 journal、fence、线程错误及初始化 child 之前透明转发。watch 由既有七根及唯一 source owner 发行，并随实际 first/same-run/cross-run source-open 切换；源地址或 class 相同不构成发行权限。当前 PlayerWatch 只保存 player/class，不能把 player 地址当 AudioSource 身份使用；新增 source 身份必须来自那组实际独立根及明确版本的 watch 合同。
5. 首次宿主四根、初始化 hold、唯一 consumer、typed permit、joint drain、后续 Pause 与唯一 Clock 的顺序保持。旧 watch/旧 source 清理不能撤回新 source；相关 worker 调用须先关闭权限并保留实际责任，不能在 worker 做 owner-thread 图捕获。
6. 原 native source helper 的进程物理 slot 表上限为16，并非每个 owner 独享16个。构造函数加这六个入口后有18个 source 入口及独立GetTime；须按最终清单明确扩容并同步 native/managed slot 验证、跳板表和容量反例。当前D实现扩为source全局32槽；不可缩减既有入口来迁就容量，也不可只修改成功计数。

## 实际验收与交付

使用具有独立固定身份的新 owned fixture；recipe 重编不会自动取得旧 fixture 的 CALL 授权。先建立 formal Module/Service/双 SDK 正向速率控制，再从独立 native caller 和实际 NativeRuntime.Call 进入每个新入口。在原 store/engine-call 前核对旧 Clock、source、prepared Verify/Commit/Restore 及 SDK control lease 已失效；原函数仍恰好执行一次。

至少覆盖同值、distinct、恢复、raw 参数、observer 失败、原函数异常、worker、重入和在途停用；foreign source、外来 worker、伪造 MI 和高次数调用不得增加 playback journal 或使当前源 Unknown。分别验证 first-capture、同局初始化和不同局 source-open 后的 watch 切换，以及迟到旧清理。

冻结源码和实际构建输入，记录 native 与 managed fresh build、PE/PDB 对应、确切命令/PID/exit/JSON，保留失败现场。新功能定向通过后再做组合28套、SDK、solution、六Release及零游戏调用的固定映像验证。所有数字必须对应最终源码的实际执行。

离线阶段不安装或启动真实游戏，不操作 UI 或账号。此任务完成仍不能证明引擎内部通过已解析 icall 指针、AudioPlayable 或其他 native 通道的完整覆盖；真实生产首次准入、任意 Seek 的玩法状态重建、effect 完成和 B01–B19 仍须独立完成。

同时保留一项后续范围：当前既有 source native Enter 除新构造器筛选外仍在 managed 图过滤前推进共享 fence。新六入口的 foreign 正向通过，不能据此宣称原有 SetPitch/set_clip/Pause 等来源也已具备精确 receiver 隔离；它们须按最终相关性合同另行实证并补齐。首次捕获前的宿主关联也不等同已存在的七根 source watch，不能以这一增量放开任何前置捕获。
