# 主代理自查

2026-10-23。本批由root整合、自查和完整构建；三名原子代理仍为额度错误状态，未将它们记为本批独立审计。没有第二runner；以下只描述实际完成的检查。

1. 检查native frame/config/stats分别48/40/48字节、两类原函数签名、不可重配slot、native finally与entered/exited/inFlight范围。原函数不经过托管delegate；观察者无skip/replace权限。
2. 检查broker首patch/ABI/别名准入、发布前强持entry/sink、unknown安装不重试、worker fault无Gate锁/原生对象读取、物理重入独立捕获、逻辑解绑后的上下文清理及stats sticky unknown。fault回调被明确限制为托管身份/原子状态。
3. 实际Dobby回归初版只比较Caller位于fixture PE；已增强为汇编导出精确返回label，并使Start经过tail-jump relay。最终完整Runtime138包含增强断言、同步首Move、rawAL/fullref、两类SEH/C++与阻塞worker/解绑；全部通过。
4. Hooks新8组定向首次即125/125；最终完整构建125/125。完整25套1324/1324，session67464 exit0。15条既有测试warning留存，不声明干净重建零warning。
5. 静态脚本未加载样本，固定SHA与三输入hash，9个范围、1574条指令、3.1/3.0常量均再次读取磁盘核对。public wrapper绕过Managed2的事实进入resolver与下一接线约束。
6. 冻结402源输入、18暂存文件；与1315归档比较7新增/9修改/0删除。新旧两个source ZIP均逐项核对；旧1021实际安装18项、其旧326源归档和三份原游戏文件匹配。

没有把transport在途为0解释为所有未来协程消费者结束；本批没有Adapter tracker调用点。没有把WaitUntil与3.1秒Delay混为一条，也没有凭同址Release或TLS签发锁信用。下一步要求真实身份/root/终态/native退出；控制器和其他资源仍待全局退休协议。

一次构建调用误用了不存在的PowerShell路径，未启动构建或写出build.log；改为当前PowerShell执行同一Build.ps1后完成。不存在一份失败包被最终证据覆盖的情况。native-targeted与Runtime初版定向日志保留为过程证据，只有最终build.log/冻结ZIP代表1324。
