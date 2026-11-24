# 本批之后仍需完成的连接

本批目标为生产预初始化六区发布、V2 完整安装记录和生产 C# 只读采用。该连接即使通过隔离映像验证，也不代表游戏冷启动、实际 CLR listener 回调、IL2CPP 根和整局退休已经验收。

后续先实现内部 listener 回调桥：从真正 NativeFirstInstall owner 取得完整 lane identity，一次附加并永久保留 delegates；按实际父、子 sequence 与 broker 私有物理调用关联。Before、Returned、Abnormal 必须保留严格配对；TargetReadFaulted 的 Abnormal(token0) 可以没有 Before，不能合成已经 capture 的托管对象或正常结束。frame 与机器状态只在回调内复制，不跨回调保存借用指针。

这一步的实现与证据至少包括：32字节observer/176字节frame的精确布局；采用前后V2及lane全identity核对；同一native lane只Attach一次，失败仍永久保留回调根；外来父/worker/溢出不能借用祖先context；旧调用跨Attach不继承新observer；正常、原SEH/C++、Before/After故障和TargetReadFaulted无Before的清理；logical detach与GC之后的在途回调保活。原生V1父descriptor和listener descriptor仍是两个快照点，发布凭据V2没有把它们合成一个observer；如需聚合快照，另立版本合同和跨版本旧调用证据，不能改称本批已经保证。

再把真实 invokable 执行接回 UnityEvent ledger，核对当前父调用、事件、列表、索引和对象根。invokable dispatch 不能代替全部 delegate leaves；动态 listener、外来父调用遮蔽、溢出、缺 post、完整异常记录与连续 mutation/provider 转交/取消仍需各自证据。来源未封口保留 Pending，不能从前后图相等推断全部 future 已结束。

全部 future 与消费者确实结束后，再组合 VerifyConsumersEnded、Close 和 native、图、音频、globals、菜单、result、settings 的资源退休，并验证完整 Retry、Return、Loader。用户“实测稍后”仍有效。完整 B01–B19 继续，仅既有 B03 整项与 B18a 子项勾选；RequestPlay 保持关闭。
