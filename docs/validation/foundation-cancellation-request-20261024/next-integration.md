# 下一步集成与验收

2026-10-24。[本批完整回归](build-summary.json)已通过 25 套、1575 项。下一步的重点是把已验证的有限通道接到真实运行合同，再形成可审计的取消完成和资源退休凭据。当前 [scope.md](scope.md)中的 false 边界仍有效，以下均未实施完成或未验收，不据测试数扩大整体 19 项的勾选。

## 1. 验收真实发布窗口与固定游戏接线

分别完成 12 个 mutation 和 6 个 Stop 入口的真实 metadata、参数类型、hidden MethodInfo、代码身份与永久 slot 责任验证。自有目标已经证明三类 Dobby Prepare/Configure/Commit 的有限路径；尚需针对实际生产窗口证明首写之前没有合法并发调用，并验证部分解析/配置/提交失败后不重装、不丢弃已发布责任。现有九写 listener receipt 不覆盖这 18 个入口，不能直接借其 Ready 宣称新入口已安全发布。

用户恢复游戏实测后，验收同一次真实冷启动中的 receipt、listener/mutation/Stop callback、实际 tracker provider 和 ledger：使用实际 receiver、Enumerator/Coroutine、owner/代次及 IL2CPP 根。自有 CLR fixture 中的受控 graph、root、provider、初始化 idle 必须逐项替换为生产路径，不能因已经执行实际 ledger 就省略对象后端验收。新增故障注入还应覆盖 observer 部分安装、真实 Root/Target/Class 失败和发布后 Unknown 保责任；这些不应与受控回归混称一个证明。

## 2. 在 Pending 保留之上建立引擎取消证据

本批已能观察请求并阻止 Ready/退休。后续应独立关联引擎调度器注册与移除、实际 MoveNext/Dispose、同 receiver 的所有停止分支和未来 Start。状态继续区分 Requested、NativeReturned、Abnormal、Unknown 与 EngineCancelled；Stop 返回、Coroutine handle 释放、共享 Dispose 地址或静默若干帧都不能直接证明取消完成。

保持 StopAll 的 entry 快照与退出时集合重核，新增 Start 或槽代次变化不能继承旧请求；string 形式应先建立可信的实际名称到实例关系，未建立前保留 MatchUnproven。wrapper 的 source/native sequence/parent 继续分别记录，不能按嵌套次数生成多个逻辑取消凭据。只有取得绑定实际 owner/代次的引擎完成证据后，才设计能解除 Pending 的新合同；本批没有这种解除接口。

同时验证同步 native 回调内新 Stop 的最迟到达时机：在 Ready 前、状态发布后、每次 native read 后、首次根 free 中及下一根前均停止后续推进。当前屏障的受控测试需要扩到实际生产对象后端与引擎调用；完整异常 flags/context、并发首发和任意部分写失败仍是独立门槛。

## 3. 继续封闭 listener 与资源生命周期

将动态 delegate leaf 的真实执行和跳过、序列化同址 alias、generic/inflated/shared 实现、直接字段写与 provider/loader 转交纳入身份和生产者责任模型。上层和下层操作可能内联或嵌套，保留逐次物理记录，同时避免把它们叠加成不同逻辑变化。最终列表恢复相等仍不能说明中间没有发生变化，也不能说明全部 future 已封口。

最后联合取消完成、listener/leaf、provider、Loader 和 native 在途调用验收 Retry、Return、再次进入及退出菜单重建：先停止新生产，确认在途配对、借用者退出及旧资源不再可达，再提交退休和逐项释放。把正常、异常、停止失败、部分根释放后 Pending 到达分别核对。单个协程自然结束、本地 Outstanding=0 或几次活动归零，都不能代替整个 run 的资源结束凭据。
