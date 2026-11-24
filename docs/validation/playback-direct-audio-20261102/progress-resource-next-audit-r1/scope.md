# 审查范围

auth: 父任务明确授权的离线只读源码/既有证据/固定映像静态审查；报告仅写本目录。

in_scope: 固定 SHA853d GameAssembly.dll 的 ProgressControl.set_Clip@4016F0、同址共享声明、GC屏障、Start消费链、当前source覆盖及一个下批合同。

network_profile: offline；未连接游戏、账号或外部服务。

out_of_scope: 修改主仓/产品/已有冻结输入，启动长测，安装或启动游戏，执行游戏原函数/DllMain，游戏UI及账号，撤销B账号。未操作原SDK driver4104。当前T/formal-r2终态仍由root收尾。

只读输入原件和hash见 source-manifest.json、fixed-bodies.json、metadata-excerpts.json、pe-survey.json。直属skill共享资源缺失时使用reverse-skill-router内同名资源；旧tool-index记录IDA未安装，本次使用已有pefile/capstone，未安装或启动IDA。
