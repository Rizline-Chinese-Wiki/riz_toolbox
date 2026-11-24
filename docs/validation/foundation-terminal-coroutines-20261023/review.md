# 主代理复核

2026-10-23。root实现、整合、完整构建与自查；原三个agent仍额度错误，无本批独立子代理或第二runner。

1. 静态确认ControllerOver调用String Start@153D490，已有IEnumerator入口确实不足以捕获首个Cutout；新增Factory ref0种类与named resolver，不用签名不匹配的旧Start/Bool transport代替。Start0/Move1值及旧frame/config/stats布局保留，Factory2槽不可重配。
2. 原生factory直接调用typed original，完整引用/Caller、finally与inFlight覆盖上下文清理。Runtime139包括新增真实Dobby Factory case：模拟named链精确返回label、full64/null、SEH/C++、worker/解绑；旧Start/Move/初始化/Judge真实fixture继续通过。Factorycase的native模拟不冒充Unity反射验收。
3. tracker/backend激活前已持有process owner/预留记录与根槽，生产6物理入口复用同patch。String name与factory返回enum是不同对象，实际enum经exact nested class和`<>4__this`核对后才发布。Cutout caller及result claim窗口相符；actual result capture成功缓存前必须完成此发布窗口。
4. 已沿实际指令修正自然完成规则：Cutout和Fade都必须先观察state1 yield，再正常false/state-1；Fade可以重复state1，Cutout不能重复。枚举首帧可触发别的factory，需独立记录；foreign named子调用mask只用标量、不读外来name，不能把子factory归到外层。
5. controlled backend不会取得production IsFor，生产native result capture缺同session tracker在native invocation前拒绝。2组组合验证成功及缺Cutout/原函数失败留claim/root、once-only，不回退stock。13组tracker覆盖身份/状态/配对/重入/worker/容量/unknown，Factory Hook另有1组，全部旧回归保留。
6. current observed-end没有永久锁定：后续新factory重新使它Pending。未实现活动root退休/记录复用，累计16次上限在第17次新root前撤权留责任。只提供unused-before-activation清理；不由当前inFlight=0或Destroy请求签发整局释放。
7. 最终session73370 exit0，1360/25，Runtime139/Hooks127/Audio483。完整增量0编译诊断；前置Audio有15条既有warning保留，不说干净重建零warning。定向native helper/fixture、Runtime/Adapter/Audio三轮/Hooks日志均留存，未覆盖成一份虚假的一次性全新实测。
8. 冻结408源与18暂存，较1343增3/改17/删0；两源ZIP及新stage逐项核对，旧1021安装18项/旧326归档/三原游戏hash匹配。静态10范围/957指令及三输入hash、6候选实际call字节再核对；第一次只扫.text遗漏il2cpp段，脚本已改全executable section并明确direct-E8局限，不用曾经空结果推断不存在调用。

未开启RequestPlay、安装或操作游戏，也未提交/推送。下一步是actual active失效/退出协议与全部future消费者结束，再释放图与coordinator和组合Retry/Return/Loader；B03/B18a之外没有新增勾选。
