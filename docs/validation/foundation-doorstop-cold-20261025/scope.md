# 生产 Doorstop 冷启动接线范围

auth：用户授权继续完成 PC 模组公共底层，并使用代理协作和自有测试。真实游戏按“先继续代码，实测稍后”暂缓。root 负责唯一构建和运行，其他代理分别实现调用器、独立核验器和固定游戏磁盘资料。

in_scope：在固定 UnityDoorstop 源上增加显式 `-FixedSystemWinHttp` 候选，保持实际旧代理的 82 项名称和序号，四个必需 WinHTTP API 接入 canonical 转发。自有 Host → Importer → winhttp.dll 使用普通静态依赖；真正 DllEntry 在 CRT/日志前初始化，Importer 的真实 TLS/C/C++/DllMain 随后只运行两个具体无效参数调用。完整四 API 比较和工作线程仅在 loader 返回后运行。

network_profile：NO_PROXY 会话、当前设置只读查询、无 PAC/WPAD 的本地参数拒绝；调用器不使用 Connect/OpenRequest/SendRequest，不访问游戏服务或修改设置。设置字符串只落加盐摘要、长度和空值；没有抓包，不声称系统内部零流量。

运行范围：自建二进制及其独立进程，有限等待和仅该进程对象终止。新输出都位于 `C:/Users/71957/riz-build-cache`。缺代理与旧代理在临时部署目录复现，实际宿主必须给出明确身份拒绝，异常或超时不能算通过。构建输入持禁止写删的只读句柄，前后 hash 一致；正式源冻结 626 项，历史 613 项中4项明确修改、13项新增。

只读游戏资料：核对固定 EXE、UnityPlayer、GameAssembly 和 metadata 的文件 SHA，提取四个引擎入口的完整 body、pdata/unwind、ABI 和已知字段来源；不加载或运行游戏，不授予 V4、首次发布或 Ready。Release 三段 RF 原样保留。metadata 格式未识别，不猜其版本。

边界：其余78个旧转发保留既有来源选择，不据此宣称完整 WinHTTP/Version ABI 验收。候选在自有程序的 DllEntry 中成功，不自动证明所有 loader 环境安全或真实游戏启动顺序。生产新核采用、V4、真实对象与GC、调度、取消及资源退休仍须后续实现；完整 B01–B19 状态不因本批局部验证而改为完成。
