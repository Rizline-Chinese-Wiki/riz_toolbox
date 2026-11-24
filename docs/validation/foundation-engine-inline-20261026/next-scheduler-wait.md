# 下一批：scheduler 行与 wait 发布责任

2026-10-26，普通固定 PE 逆向，`flavor = null`。**先实现 scheduler 的逐行登记→消费/删除→callback/cleanup 排空；wait 同时已有可接的真实普通派发函数 `UnityPlayer+5A5260`，不必继续泛搜所有协程。** 两条账本各自维护代次和物理调用；已有 Start inline 仅提供节点清理事实，不替代它们。本稿不发取消完成凭据，也不将 Pending 的保守保留描述为底层已完成。

范围继承 [生产研究范围](../foundation-engine-production-20261025/scope.md)。本次仅阅读现有证据和固定 `D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll` 的磁盘 PE；没有加载或执行 DLL、游戏、测试产物，没有启动服务、GUI、构建或改写历史证据。使用 ida-reverse / docs-generator 技能；工具索引确认 IDA 不可用，沿用现存 Python 3.14、pefile 2024.8.26、Capstone 5.0.6。这里只新增本文件。

## 证据与本次新增事实

以下哈希为本次实际读取内容。RVA 均相对固定 UnityPlayer，函数范围左闭右开。

| Evidence | 资料及 SHA256 | 有限用途 |
|---|---|---|
| E1 | UnityPlayer.dll；`43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1` | AMD64，31,006,120 字节，ImageBase `180000000`；仅磁盘读取 |
| E2 | [scheduler-evidence.json](../foundation-engine-binding-20261025/research/scheduler-evidence.json)；`64ed08c89d488ab8dba3572c406a848e558e52e946cbe04567353b1db3e9f65f` | 登记、删除、callback、Continue、节点清理完整函数 |
| E3 | [scheduler-consumption.json](../foundation-engine-binding-20261025/research/scheduler-consumption.json)；`e4d2f484362fee99ebd37efa0c30e7a94f1d2c6d0c55c5d77d8a8e7581e8ac76` | pump、消费、整体销毁、yield/wait、共享引用完整 CHAININFO 合并范围 |
| E4 | [scheduler-xrefs.json](../foundation-engine-binding-20261025/research/scheduler-xrefs.json)；`d2af8b2f7c47d341d25505e8be51f251b3f3f66fc4d76b758954758ea32c9e95`；[contract](../foundation-engine-binding-20261025/research/scheduler-contract.json)；`f9ff4d13c1387a8ab6984659357acdb65ecfa2a463e4e04d8393b13e12d8e078` | 限定直接调用/RIP 引用、48 个旧指令断言；不代表全部间接引用 |
| E5 | [上一批下一步](../foundation-engine-production-20261025/next-native-completion.md)；`338649530e3a4ca0fad34c3de73efe754fa70e4653278202f1eaa880c22de569`；[调度研究](../foundation-engine-binding-20261025/scheduler-cleanup-research.md)；`b4e8fd96102e972ca6b3ace4588b20671e985a0de17a9d6dedb82ac3e81d63e6` | 已核边界及上轮未闭合的普通 wait 分发问题 |
| E6 | 本次按 E1 `.pdata` / CHAININFO 独立补读下表函数，并核指令边界 | 新的普通 wait 派发事实；关键字节和复核命令保留在本文，不重写 E2–E5 |

| E6 完整函数范围 | 完整 body SHA256 | 观察 |
|---|---|---|
| `5A5260..5A5343` | `7143eab8a6af6b584a341b1b41857b7ce9f74a4108987508ca803bdab1d11ccf` | object callback/cleanup 派发，再触发另一个 managed 完成通知路径 |
| `152540..1525D1` | `301335687736833f3e3d8c4044d6ce95bc108353cf973fae9ba65e42e2c64237` | 输入 RCX 指向 owner ID；ID 为 0 直接返回 null，否则查 native owner 映射并可能走 fallback |
| `5DACD0..5DAFD2` | `2e17bb4737d2a10ae76126c8c8507748a757f52a4bf8ff0eb0795492ce317ffa` | manager 取队首对象、调用虚函数、从队列移除并发完成通知；并非全部 wait 的全局封口证明 |

## F1：scheduler 最小账本有四种移交

`status = validated(static)`，`evidence_ids = [E2,E3,E4]`。下列是已发现路径的最少完整责任集合，生产覆盖仍未验证。

| 路线 | 实际 ABI / 指令 | 要记录的事实 |
|---|---|---|
| 登记 `44D630..44D756` | RCX=tree，RDX=out slot，R8=source record；`44D743` 写实际 tree node，`44D746` 返回 out slot 地址 | Entry 复制 source 已定义字段，正常 Exit 只读 out slot 一次，创建 `tree + entry + registrationGeneration`。RAX 是指针，不能按 bool/AL 接收 |
| 删除并 cleanup `447AD0..447C26` | RCX=manager，RDX=record，R8=entry；record=`entry+20`；`447B33/447BF5` erase，`447C19` 间接 cleanup | 删除和 cleanup 分开配对。Entry 快照，原函数返回后不再读已回收 entry 或可能已释放 node |
| 正常消费 `447C30..447D60` | RCX=manager，R8=entry；RDX 实际进入后被覆盖，无已证有效语义；`447D3F` erase | 将“可调度行”移交为“已消费、callback 待交付”的物理责任，不能直接清零 |
| 整体销毁 `447750..4478AC` | RCX=manager；`447840` 直接 erase；`447864` 调 cleanup，绕过前两种函数 | 必须逐行记录。整体函数返回或 manager 元素数为 0 都不能代替逐行 cleanup 和在途窗口 |

source record 已定义字段为 `+00 double dueTimeBits / +08 int64 frame / +10 float intervalBits / +14 byte repeat / +18 payload / +20 callback / +28 cleanup / +30 uint32 ownerId / +34 uint32 category / +38 uint32 creationTag`，entry 中相对偏移加 `20`。不要复制 padding 成语义字段。coroutine 记录需核 `payload=exact node generation`、callback=`78F5E0`、cleanup=`78F680`；同时保存实际 tree/manager，不能只按裸 payload 地址认领。

manager 的 `+30` 是 tree，`+38` 是成员数，`+40` 是 pump iterator，`+48` 是 tag。全局指针槽为 `1CB3C00`，本次没有读取其活体值，也没有证明只有一个实例。tag 和 node 引用数均不能替代本地单调代次。

## F2：消费结束后 callback 还没有开始

`status = validated(static)`，`evidence_ids = [E2,E3]`。pump `[447D60,447FAF)` 实际为 RCX=manager、EDX=category mask；其 return 寄存器没有已证源码类型，wrapper 应保原生 passthrough，不能制造 bool 语义。

| 分支 | 精确 call / return PC | 回执要求 |
|---|---|---|
| 非 repeat | `447EDF→447C30` / `447EE4`；`447EEA: 41FFD7 call r15` / `447EED` | 删除正常返回后仍保 `ConsumedPendingCallback`，用实际 native pump scope、该条 consume 序号与 callback 的真实参数关联 |
| 非 repeat cleanup | `447EF2: 83F801 cmp eax,1`；`447EFA: FFD6 call rsi` / `447EFC` | callback 的完整 EAX 等于 1 且 cleanup 非空才执行；其他返回不能伪造 cleanup。此回调可已在 Execute 内做 Release，分开核实际子调用 |
| repeat | `447F3B→44D630` / `447F40`；`447F46→447C30` / `447F4B`；`447F51: 41FFD7` / `447F54` | 先产生新登记，再消费旧代次，再 callback；旧行与新行必须各有代次。这里没有非 repeat 的条件 cleanup，不得自动给复制行增加一次 node 引用 |
| owner 不存在 | `447F60→447AD0` / `447F65` | 转到删除+cleanup；owner 查找失败不等于 cleanup 已经返回 |

新增 callback 外层 `[78F5E0,78F673)` 的机器 ABI 必须是 `uint32(nativeOwner,node)`。owner 与 node `+58` 不同会返回 EAX=1，完全不进现有 Execute；相同则 `78F66E` 尾跳 Execute。只观察现有 Execute 会漏 owner mismatch 的 callback 窗口。callback/cleanup 的 After 一律用 Before 缓存和真实返回，不重新读取 node。

**最短接线 P1**：新增独立 scheduler 原生传输家族和 managed row ledger，覆盖 `44D630 / 447AD0 / 447C30 / 447D60 / 447750 / 78F5E0` 六个真实函数。保留现有四入口和 inline ABI，不复用 reserved 字段。每个 native scope 生成自己的 call/parent/root identity，unknown/foreign 也占物理栈，不能由调用方传一个 StartSeq 来借祖先身份。登记、consume、callback 和 cleanup 关联都以当前实际 scope 和 exact row generation 为依据。

为了逐行覆盖 `447750`，仍需两个独立内部观察点：`447787`（刚取当前 RBX entry，尚未可能 callback）复制行并建立 native row scope，`447866`（erase 与可选 cleanup 均正常返回之后）关闭该次 row scope。`4477EE` 的 category `0x40` callback 在 erase 之前，可能重入产生/撤销别的行；要保留正常 callback、cleanup 子调用及原函数异常的身份。每轮出口之后才可退该行，不能把一次 manager scope 伪装成所有行已配对。内部 RVA 不是函数 ABI；安装前仍须另证固定重放跨度、relay、寄存器/flags 和 unwind，本稿没有给它们签发 patch 权限。

可选的通用 `3F01B0` erase hook 能汇集几条删除调用点，但它服务其他 tree，扩大 foreign mask 与来源判定面；它也不提供 callback/cleanup 完成。下一批优先上述六个窄函数和逐行探针，不为节省一个函数入口牺牲 exact 来源。首次实现可只收已核 coroutine tuple，通用 repeat/category `0x40` 行仍必须透明保真并保留 foreign scope。

## F3：普通 wait 分发已定位到真实函数

`status = validated(static)`，`evidence_ids = [E1,E3,E6]`。E5 未知的普通分发现在有具体实现，不能再笼统写“完全找不到派发 RVA”。`[5A5260,5A5343)` 的 RCX 是实际对象，RDI 留存同一对象。其关键指令为：

```text
5A5269  4883791000           cmp  qword ptr [rcx+10],0
5A5273  4883C128             add  rcx,28
5A5277  E8C4D2BAFF           call 152540
5A527F  740A                 je   5A528B                  ; owner null 跳过 callback
5A5281  488B5720             mov  rdx,[rdi+20]            ; actual payload
5A5285  488BC8               mov  rcx,rax                 ; actual native owner
5A5288  FF5710               call qword ptr [rdi+10]      ; return PC=5A528B
5A528B  488B4718             mov  rax,[rdi+18]            ; callback 后重新读取 cleanup
5A5292  7406                 je   5A529A
5A5294  488B4F20             mov  rcx,[rdi+20]            ; callback 后重新读取 payload
5A5298  FFD0                 call rax                     ; return PC=5A529A
5A529A  48C7471000000000     mov  qword ptr [rdi+10],0    ; 仅清 callback
```

这里与 scheduler 的行为不同：callback 的返回 EAX 不控制 cleanup；只要起初 callback 非空，且稍后读到 cleanup 非空，即便 owner 查找为 null 也会 cleanup。它仅在 callback 非空的外层分支内运行以上逻辑；不能因 cleanup 字段非空就推断调用发生。原始异常可落在 callback 之后、cleanup 之前或清 callback 之前，必须各保未闭合责任。

`5A52A2` 之后还按对象 `+30/+38/+40` 找 managed 对象，`5A52F5` 从 `8392B0` 结果 `+1F0` 取方法信息，`5A52FC→7740C0`，`5A530E→3B4210` 进入另一条通知路径，随后清 `+30/+38`。本次没有把该 managed 方法名或所有订阅者补成猜测；但**物理 wait dispatch 不能在 `5A529A` 就算全部退出**，应保留到真实函数 Complete/Dispose。这个后续通知也不能自动算作 launch 的全部 future producers 已封口。

新增入口不是只靠字段形状猜到的：实际数据指针 `199EE50→5A5260` 位于函数指针序列 `199EE18+38`；`5E4898` 以 `LEA` 取 `199EE18` 并在 `5E489F` 写入所构造对象的 vtable；其 `+08` 是 `5DB600`，`+38` 是上述派发。`[5DACD0,5DAFD2)` 在 `5DAFB2` 写所取实际队首对象 `+48=2` 后，于 `5DAFBA: FF5038` 调其 vtable `+38`，真实 return PC 为 `5DAFBD`。这给出一个实际 manager→对象虚通知→共同派发的静态路线。**没有证明每个 yield 对象都来自此 constructor、所有对象都用此 vtable，或所有 manager/虚函数重载均覆盖。** 没有从 vtable 邻接关系推成完整活体类体系。

还核到两个 E8 直接调用者：`7FF3C0→5A5260`（完整主函数起点 `7FF040`，该 RF 为 `7FF358..7FF3E5`），`C21BFD→5A5260`（RF `C21BF0..C21C4F`）。扫描只认 `.pdata` 解码后的实际指令边界；本次没有扩大为全部间接调用者证明。

## F4：wait 发布、停用、撤销和派发结束是四件事

`status = validated(static)`，`evidence_ids = [E2,E3,E6]`。

| 事实 | 实际指令 / 来源 | 最少新记录 |
|---|---|---|
| 发布 | `78FFC9` 取实际 yielded wrapper `+10` 得 wait；`790069` 写 wait owner ID，`790073/79007E/790082` 写 callback=`78F5E0`、cleanup=`78F680`、payload=node；`790098` 写 node `+78`；`79009C` lock inc wait `+0C` | `WaitPublishBegin/Committed`、actual wait/node generation、owner ID、tuple、实际 native producer scope；不能把每次进 `78FC50` 都算发布 |
| 替换旧 wait 引用 | `790086` 先读旧 node `+78`，非空时 `790093→342400(old+8)`，返回 PC 为 `790098`，然后才写入新 wait | 单记旧 wait 的 `ReferenceReturned`；此处没有直接清旧 wait callback/payload，不能把 node `+78` 已换成新对象当成旧发布已撤销，也不能假定旧对象已析构 |
| 仅停用 owner | Detach `7938B3` 或 `793983` 只写 wait `+28=0` | `WaitOwnerRevoked`；callback/cleanup/payload 仍存在，既有 dispatch 继续保留 |
| 完整字段撤销 | Release `78F6F5..78F700` 清 wait `+28/+10/+18/+20`，`78F70C` 归还 wait 引用，`78F711` 清 node `+78`；Start 已核 inline 内 `792527..792543` 对应同一序列 | `WaitRevokeBegin/FieldsCleared/ReferenceReturned`；清字段前复制实际 wait 身份，最后引用可能销毁对象，After 不能再读 wait |
| 普通派发 | `5A5260` 上述读取、callback、cleanup、清 `+10` 和后续通知 | `WaitDispatchEnter/Exit`；实际 callback/cleanup 由 `78F5E0`/现有 Release 的真实 token 和 caller PC 关联，不能从 Start 返回猜测 |
| manager 批量清理 | `[5DA510,5DA6DB)` 内 `5DA630` 取容器 `+190` 中对象；`5DA645` 调其 cleanup(payload)，`5DA648` 清 `+10`，`5DA65B` 归还对象引用 | 逐对象 cleanup/withdraw scope；与普通 dispatch 分开，不能只看容器为空 |

**最短接线 P2**：先新增 wait publication ledger 和独立 `5A5260` 外层真实物理 scope；身份是 `wait address + publicationGeneration + exact node generation`。`790069` 前的发布 Begin 与 `7900A0` 的完成分支记录此次真正执行写入的 tuple，异常/中间退出保留“部分发布”；所有后续写者都必须按该代次归属。`78FC50` 的宽入口和返回不足以区分 callback 已占用、isDone、fallback tree 和成功 wait 发布，不能只挂其 After 读取字段。

停用/撤销点仍须专门旁路事实。复用当前四函数的物理 parent proof，不用 Release 返回或已有 inline End 反推 wait 地址：现有 Begin 没有保存 node `+78` 的 wait identity。需要独立 before-clear 快照和 after-clear/ref-return 回执（或等强度的真实阶段证明），而不是修改旧 112/32 或 144 ABI 的 reserved 位。旧 wait 被替换时应保存 `790086` 的实际旧对象和归还回执，不能按新 wait 覆写同一个 node→wait 字典而丢旧代次。callback/cleanup 子函数入口给出真实调用参数和 caller PC，只有 exact current wait-dispatch / drain-row native scope 才能签关联；nested foreign dispatch 必须 mask，不能借祖先。

callback 会重入并可能改变 wait 字段；`5A528B/5A5294` 实际再次读取 cleanup/payload，因此 entry 的 tuple 不是随后调用的天然不变保证。必须将所有已知发布/撤销写者的代次变更与实际子调用结合；若一次读取之间有写者、无法唯一归属、未知 target 或未证并发，应保 Unknown，不把 Entry 缓存冒充原函数真正消费的 target。下一轮补齐字段写者/串行语义之前，P2 只能出候选事实。

## 执行顺序和拒绝授完成的门

1. **M1，scheduler 传输和 row ledger。** 先做 P1；原参数与完整返回保真，native 先登记责任再复制标量，异常 finally 发 abnormal 并保 Unknown。每个 row 至少有 Registered、ConsumedPendingCallback、CallbackOpen、CleanupOpen、Retired/Unknown，不能只设一个 bool。测试包含同 node 多行、地址复用、非 repeat EAX 高位、repeat 先登记、owner 缺失、consume→callback 空窗、callback 重入登记、整体销毁和 callback/cleanup 原异常。
2. **M2，wait 发布/停用/完整撤销和普通分发。** 按 P2 加精确阶段及 `5A5260`；测试 owner null 仍 cleanup、callback 改写 cleanup/payload、清字段后旧 dispatch 未退出、新发布复用对象地址、partial publish、最后 shared-ref 归还、nested/foreign/worker。新增探针各自做 native+CLR 联合验证，不能用受控 frame 测试数替代真实机器窗口。
3. **M3，收齐覆盖和源封口后才设计完成转换。** exact Stop 原因/节点代次、节点普通或 inline 最终清理、每条 scheduler/wait 记录和调用排空、nested 引用、所有外层 Start/Execute/Detach/Release duty 都独立满足；run/tracker 的 future producer 还须撤销并阻止再次发布。没有活跃记录快照只说明“当前看见的数为零”，不是以后不再产生记录的凭据。

明确仍缺：所有 tree/manager 实例及写者；所有 wait 虚派发和不同通知重载；批量清理容器与具体 yielded 对象的生产归属；wait 字段全部写者与实际线程/锁的一致性；继承/自定义 yield 路线；活体 IL2CPP root/对象/owner 失效；新增入口安全首次发布和异常透明性；真实游戏动态配对。E6 新函数关闭的是“没有普通分发候选”这一研究空白，**没有关闭这些覆盖缺口**。scheduler 可以先独立落地，但 wait 未闭合的节点仍不得签 EngineCancelled；也不能把局部节点完成提升为整个 launch Pending 已解除或资源根已可释放。

## 只读复核本次新函数

在任意目录执行以下命令，只读取固定 DLL 并输出函数范围、hash 和指令，不创建证据文件，不运行旧脚本：

```powershell
@'
from pathlib import Path
import bisect, hashlib, struct, pefile, capstone
raw = Path('D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll').read_bytes()
assert hashlib.sha256(raw).hexdigest() == '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1'
pe = pefile.PE(data=raw, fast_load=True)
d = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
b = pe.get_data(d.VirtualAddress, d.Size)
rows = [struct.unpack_from('<III', b, n) for n in range(0, len(b)-11, 12)]
starts = [r[0] for r in rows]
def primary(row):
    for _ in range(16):
        h = pe.get_data(row[2], 4)
        assert h[0] & 7 in (1, 2)
        if not (h[0] >> 3) & 4: return row
        row = struct.unpack('<III', pe.get_data(row[2]+4+((h[2]+1)&~1)*2, 12))
    raise ValueError('Unwind chain bound exceeded')
roots = [primary(row) for row in rows]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for entry in (0x5a5260, 0x152540, 0x5dacd0):
    i = bisect.bisect_right(starts, entry)-1
    assert i >= 0 and rows[i][0] <= entry < rows[i][1]
    merged = []
    for lo, hi, _ in sorted(row for row, root in zip(rows, roots) if root == roots[i]):
        if merged and lo <= merged[-1][1]: merged[-1][1] = max(hi, merged[-1][1])
        else: merged.append([lo, hi])
    for lo, hi in merged:
        code = pe.get_data(lo, hi-lo)
        ins = list(md.disasm(code, lo))
        assert sum(x.size for x in ins) == len(code)
        print(hex(lo), hex(hi), hashlib.sha256(code).hexdigest())
        for x in ins: print(hex(x.address), x.bytes.hex(), x.mnemonic, x.op_str)
'@ | C:/Python314/python.exe -
```

E6 的全文没有覆盖所有代码；本次辅助扫描仅筛选函数范围内的 `call [reg+10/+18]` 与附近 `+20` 字段使用，再完整补读候选函数，未将模式命中数量当作覆盖证明。各旧原件本次未运行生成脚本，未被重写。
