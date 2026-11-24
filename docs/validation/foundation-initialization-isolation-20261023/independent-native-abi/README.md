# 独立生产 native ABI 复核

在完整 build45954 成功且停止构建后，独立 runner 引用已冻结 Runtime/SDK DLL 执行，退出码为 0，编译为 0 warning / 0 error。[命令及完整输出](command-output.txt)、[运行结果](fixture-result.json)、[原始文件哈希清单](fixture-manifest.json) 已归档。清单的六个源文件在归档时均重新计算 SHA-256 并匹配，见 [完整性复核](evidence-integrity.json)。

runner 使用自己的 `D:/RizToolbox-build-cache/initialization-isolation-independent-20261023` 项目、bin 和 obj，只引用现成 DLL；未编译生产 project references，也未修改源码或测试。[生成的项目](InitializationAbi.Runner.csproj) 与 [入口源码](Runner.cs) 同时留存。

本次确实运行 C++ native caller → 真实 Dobby patched entry → 生产 Runtime replacement → Dobby trampoline → C++ original。覆盖四种新 ABI 的原参数及引用返回、原始 MethodInfo、高位指针、float 的正负零/负数/NaN 位型、native worker/递归、original 恰一次、LOCAL getter 失败 native null、Start 尾部及独立 Play 的共享 Mode 权限、在途计数和 detach/GC 保留。Play 使用真实 MASM caller 设置非零 EDX 高 24 位与 DL=0/1，并核对实际 EDX 记录及 native original 的 false/true，验证 I1 行为。

此结果不覆盖 Start 原始 C++ 方法主动抛出 SEH/C++ 异常的输送；native observer 自身会捕获测试断言异常。主动异常证据来自生产 getter 的 Before/After/Fault 回调及 worker 拒绝路径。该 fixture 也不验证实际 IL2CPP metadata、游戏对象字段、真实游戏镜像 prologue 或完整 LOCAL 局，`gameRuntimeVerified` 为 false。
