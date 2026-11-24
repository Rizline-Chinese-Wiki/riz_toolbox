# 本批范围

2026-10-23。继续完整底层 B01–B19；用户“先继续代码，实测稍后”保持，本批不操作、启动、停止或安装游戏。

实际补齐已存在 native helper 在途统计到 C# 的缺失接线，并把外层 GetJudgeControl.GetJudge、内层 NormalMode.GetJudge、Hold MoveNext 的活动汇总进 LOCAL 的准入、终止快照、图映射退休和停止判断。helper 的原生 finally 原已覆盖 original 和 Exit 清理；本批没有改其原生执行协议。

NativeJudgeContextBridge 读取实际 GetStats，元数据记录布局为 0x30。未配置返回未知；无效统计、原生观察故障计数、托管边界异常或读取异常保持未知。broker 的 capture/after/context release 失败也保留 sticky unknown，不以之后在途为零冲掉失败。物理入口活动在逻辑 sink detach 后仍可读取。

Adapter 按已验证游戏构建、声明方法和 ABI 解析三个确切目标，只查询现有物理入口，不创建第二个 patch；外层要求已有 native transport。缺任一入口或活动未知则无法宣称 ready/drained。第一次 drain 后撤下身份，再次 drain 后才能 free；新统计不签发“所有未来消费者结束”。

新增独立 C++ Judge 原函数、32 NOP 可补丁入口和 native caller/catch/Win32 worker，复用现有离线 ABI fixture DLL；既有八类初始化目标及其 18 调用 probe 保持独立。Runtime 新用例实际调用生产 Dobby/broker/bridge/helper，验证阻塞 worker、逻辑 detach、context 清理和 SEH/C++ original 退出。无游戏图像参与。

这不是完整 active 退休：菜单/锁、controller 和音频销毁、旧回调、globals、bundle、完整 Retry/Return 命令和 Loader 组合仍缺。内层 Judge/Hold 仍是既有 managed original 输送，不能把外层 native original unwind fixture 的结论推广给它们。没有独立子代理审计，本批由主代理实现并自查。
