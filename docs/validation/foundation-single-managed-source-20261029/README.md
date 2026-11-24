# Single 实际列表与逐调用的托管接线

当前 Single 原生同步源已接到正式 Runtime/Adapter 路径：从私有 FirstInstall V7 owner 采用六个 producer 导出、通过 `ReadCurrent` 复制真实 TLS 帧、核实际 native parent sequence/managed token/代次，再对 event/list/invokable/site2 实际 RDX array 先批量取根。真实 backing array 和全容量元素也在元数据检查之前取得独立根。三类 site 分别验证 +0x198 Single、直接 0x154e5b0 零参数、+0x178 object[]；cached Single 的固定有效 bits 与动态 Invoke bits 分开。

fresh 隔离源码实际通过 **42/42** 新 Single 检查和 **17/17** 现有 typed/completion 定向回归，两个 build/test exit 都为 0。冻结 622 份编译输入，构建前后哈希相等，20 个本包工作区源码输入与冻结值一致。

**这不是完整 effects 闭合或真实游戏验收。** 本包新 42 项中的帧值验证为受控数据；7 项列表检查经过生产 `NativeRuntime.Root` 和官方 IL2CPP 导出 seam 的非托管存储夹具，4 项经过实际 WorkOwner typed handler；没有声称这些检查实际运行了 native ReadCurrent 的正向回调。root 的真实 mapped CLR Attach、native owned machine 的同步 ReadCurrent、游戏 metadata 和最终效果关闭分别有独立证据边界。

## 实际执行与冻结

最终目录：`D:/riz-build-cache-next/tween-single-managed-source-r1-20261029/`。

| 项目 | 实际结果 |
|---|---|
| SingleEvent | 42/42，exit 0；生产 Runtime/Adapter 编译无警告，新测试链接的共享 FakeNative 与未赋值 fixture 字段共 26 个编译警告 |
| 现有 typed 回归 | 16 个各自独立子进程，16/16 exit 0 |
| 现有 completion 反例 | 1/1 exit 0；没有真实 mid/list 时保持 Pending |
| AudioContent 定向构建 | exit 0，19 个已有 fixture/nullable 警告；未将本包定向通过称为完整 Audio 回归 |
| 源码固定 | 622 文件；source-before/source-after SHA 均 `fb90ef1765ca76ea71192899a19a7c23f45bdf40859c43608050551617e95890` |
| 源码 ZIP | SHA `308eb84b4165b811e0d19eb9549be9d405337d4c915c6311d3a9a47ca87f5869` |
| test-result | SHA `f27403890dcb1e28134d18da5b575a33f3688fed05b78f98d692c983ab0e60a8` |

从仓库根重现，输出必须是尚不存在的目录：

```powershell
C:/Python314/python.exe docs/validation/foundation-single-managed-source-20261029/run-checks.py --output D:/riz-build-cache-next/tween-single-managed-source-replay-20261029
```

脚本实际冻结当前源码到独立目录、建 ZIP、编译 SingleEvent/AudioContent、运行新检查及17个已有精确回归，最后重核 frozen 与 live owned 输入。它不加载游戏或 native fixture。冻结依赖是该时刻 V7 全部编译输入，不能用 N1 的旧完整矩阵代替。范围见 [scope](scope.md)，固定结果和逐项日志见 [test-result](test-result.json) 与 [evidence-index](evidence-index.json)。

## 调用及责任

1. `NativeLocalTweenBackend.BindTypedTweenEvents` 先绑定现有 Single operation capture，再采用 `NativeTweenSingleEventBridge.AdoptReady()`。Bridge 不 LoadLibrary、不 Configure，也不接 caller publication；只从 `NativeFirstInstall.RequireReady()` 取得现行私有 owner/SingleEventPublication。回调在实际可能发布的 Attach 之前永久保留；不确定的 Attach 禁止重试。
2. 同步 Notify 将原始回调地址只交给 native `ReadCurrent(actual, copy, machineCopy)`；拒绝地址/错误 layout/线程/epoch/source PC/寄存器/保留字段不能取得 Delivery。Delivery 构造另需私有 issuer，过期或复制 DTO 不能借用它。
3. `Binding.WithCurrentSingleContext` 要求当前 `_operationTop` 是这一条 Single1/slot31 已采用 native channel，字典中真实 managed token 与当前 context 完全一致，并核 native sequence、event、MethodInfo、caller 和 rawbits。外国/嵌套父屏蔽祖先；没有匹配 context 的共享全局调用不读对象。
4. mid/Before 先批量 `Root` 实际对象，再做 known barrier/metadata。新数组与元素通过立即 publication callback 取根；完整 count/version/backing/全部 capacity vector 重核，重复指针按 index 保留。function address、MethodInfo 和 vtable target 均不当对象取根。
5. site0 核官方 closed Single/cached metadata、实际 saved class+0x198 target/MethodInfo 与 official virtual resolution；动态 Single 使用 parent rawbits，cached 使用 m_Arg1 原始位。site1 核实际直接零参数 InvokableCall，RDX=0，绝不读取 XMM1 当参数。site2 核 class+0x178 的 object[] ABI、真实 RDX array 及 rooted boxed Single rawbits，保留 signed zero/NaN payload/Inf/subnormal。
6. WorkOwner 以该原 parent 保留 mid 与每次 child generation；Returned/Abnormal 只进行既有根上的标量完成职责，Unknown 后不新读对象。创建于具体 child 区间的新 SourceOccurrence IDs 接入现有工作 ledger 的诊断关联；它们不会被 typed return 消除。
7. `ReadTypedEventActivity` 合并实际 Single producer 与 operation 活动及 Unknown。native info 的 parent pool 为 **128**，嵌套 depth **32** 是另一个限制；初稿将二者混用，经真实 native GetInfo 反例后已修正并加入检测。

## 剩余边界

真实 list/count=0 已能成为同一次 mid 的观察事实；它仍不是 generic delegate writer 闭合、serialized/direct replacement 隔离或最终 native drain 的凭据。非空 listener 的 opaque/multicast/setter/协程及新 Tween 效果仍需独立 end/transfer issuer。当前 snapshot 的 Pending 会准确说明缺 mid、缺 post、没有完整 child return 覆盖、writer closure 或 effect end；没有任何新完成票/自动释放路径，也没有打开 RequestPlay 生产准入。

## 诊断与 Evidence → Finding → Path

| Evidence | source_ref / repro | 实际观察 |
|---|---|---|
| E-contract | `.docs/foundation-expansion/SINGLE-V7-CONTRACT.md`、`TWEEN-SINGLE-MID-NEXT.md` 及 native header；哈希列于 index | 200B frame、24B observer、144B info；三种 ABI 与父代次，不套 Void 帧 |
| E-owned | [test-result.json](test-result.json)、源清单、同目录日志；上面的 run-checks 命令 | 622 frozen 输入；42+17 实际通过，源码与20 owned 输入不变 |
| E-roots | SingleEvent stdout 的 `actual NativeRuntime` 七项和 `actual WorkOwner` 四项 | 真实 NativeRuntime root 导出路径和非托管完整列表存储；先 root 后 class，partial failure 保留既有责任；上下文与效果 snapshot 接线 |
| E-diagnostics | diagnostic-r1/r2/r3/r4 的实际工具输出与对应 artifacts；typed-regression-r1 日志目录 | r1 测试 ulong 编译错误已修；r2 30项、r3 37项、r4 42项中间成功。早期 targeted17第17项仅诊断文本缺 executing-list 触发断言，现已改为准确 pending 文本并实际17通过。一次误启完整 Audio 在未设 native fixture env 下实际983/990，不能算整套通过；本次明确只消费后续frozen定向结果 |

F-source，severity=n/a_re，confidence=high，status=validated-controlled，evidence=[E-contract,E-owned,E-roots]：同步 native 源/实际父、root-first完整 list 读取与3site ABI 的正式实现已编译，受控完整链实际通过；生产 ReadCurrent callback、真实游戏 metadata 与 effects闭合仍分别待验。

F-effects，severity=n/a_re，confidence=high，status=validated-boundary，evidence=[E-owned]：Return/Dispose、空 list 或多个成功 child 都不会制造效果结束；MissingPost、root失败、列表变动保留 Pending 和真实责任。

P-single，path_type=callflow：实际 pre-init V7 owner → 6exports/Attach → native TLS ReadCurrent → 真实 operation parent/token → event/list/invokable/array roots → official closed metadata/full vector/3site dispatch → WorkOwner typed invocation/SourceOccurrence facts → pending independent effect closure。E-contract→E-roots→E-owned 支撑上述实现和受控边界，未把后面的 pending 步骤写成已完成。

timeline：读取已保存合同和現有接线 → native frame/bridge和根批处理 → 30项初检 → 增7项export-backed list与4项真实WorkOwner handler → ParentCapacity128修正 → frozen622输入42+17通过 → 源码与证据索引冻结。没有游戏、GUI、安装或推送。
