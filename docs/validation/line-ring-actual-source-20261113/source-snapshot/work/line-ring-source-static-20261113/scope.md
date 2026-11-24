# Line/ring source profile 文件复用范围

本分支只实现 `src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceProfile.cs` 和本目录的静态合同/文本验证产物；实际 reader、capture、Runtime helper 和 backend 测试由主控及其指定分支负责。授权来自本次用户任务与主控明确分工；上游范围见 [implementation-contract.md](../line-ring-basis-handoff-20261112/implementation-contract.md)。

允许读取当前已冻结 C#、JSON 和 asm 文本，组合独立只读 profile，并用限定输入集的 Python 文件核对脚本复核复制准确性。禁止安装、启动或控制游戏/UI/账号、配置代理或全局环境、读取旧 ZIP、Git 提交；禁止打开新的游戏二进制、新逆向探索、构建或执行 backend 测试。脚本只检查 JSON 中原先保存的指令字节，不访问 JSON 中列出的原始 DLL/dump/metadata 路径。

目标是元数据和完整 body 证据准入，不是已观察 live ABI、actual graph、引擎 effects、投影数值或完整 Seek。全部 writer/producer/ease/init 调用缺席，CompleteSeekPermitted 仍为 false。
