# 没有前任 Clock 的首次初始化

2026-11-01。首次初始化离线增量已精确合入主仓18个文件，850个声明输入、479个产品输入（472个C#文件、6个项目、1个profile）与当前冻结一致。SourceV4／168B frame／136B info、WriterV1和InitV2／160B保持；首次捕获使用独立FirstCaptureV1／120B。完整B01–B19仍未完成。

首次路由先独立保留controller、information、Progress和Pause四个宿主根，不创建旧source、Session、publication或临时Clock。实际Start的AL1让出再保留enum、Progress两个根；同枚举器的确切clip子调用、直接字段写入、AL0和父子physical exit完成后，才能消费私有完成票。

GetTime gate→source gate共同确认实际drain，分配第一个source epoch。typed permit仅可登记一个source，条件捕获七根并前后重读图和实际get_clip；native open后才释放四个宿主根、两个初始化根及其observations。之后必须另有实际Pause/readback，依次准备dormant、构造唯一final Clock、实际arm。此时才能建立受控正式provider与SDK服务。后续同局重建继续使用此前已实现的InitV2交接。

| 验证范围 | 当前实际结果 |
|---|---|
| 首次初始化8种场景 |339项，全部actual exit0；完整正向78项含原双插件SDK变速、实际Replay、同Module/Service/SDK后续InitV2重建和Resume，最终0根|
| 根与退出 |正常保持四宿主＋二初始化，条件捕获时另有七source＋两个临时receiver，共15；native异常/未知责任保留六根，不冒充正常清理|
| 新固定owned夹具 |实际分配时Progress.Player仍为null，直接使用类型元数据；无需恢复旧播放器模板。SHA `7ef26e1c274cc1cf826fb8e7a9283e913224a6c9f9c6a35cb76103b943c19d7f`，独立封存8个确切CALL，Start clip返回RVA `2c71`|
| 新helper的原有初始化/交接 |13进程547项；普通同run拒绝、不同run正向、此前任InitV2、hold/异常/宿主失效/在途退出各保留范围|
| 原生八角色 |14进程36675项，当前native源码|
| 当前整树与构建 |28套2598/2598，solution零错误/零警告，两个独立SDK插件14项，六组件Release零错误（一个既有generated nullable警告）|
| 主仓重建 |零错误/零警告；5组PE/PDB、491文档与当前源码对应；主仓DLL实际执行first-hold22项|
| 固定真实映像静态安装 |当前源码31项、12 Prepare/Commit；DONT_RESOLVE映射，游戏方法、GetTime原函数及DllMain执行均为0|

E=实际命令/PID/exit、源与映像hash及PE/PDB对应；F=无前任首次捕获、独立保留和SDK续接；P=宿主归属→真实Start完成→native联合drain→七根→后续Pause→唯一Clock/SDK。各验证范围分别计数，不相加冒充整项底层或真实游戏验收。

完整原件：`D:/riz-build-cache-next/playback-first-capture-main-integration-r1-20261101/final-validation-r1/integration-result.json`，SHA `7074d83d7635604a79c8f7c1d1dc225e0b72c2d3f877f99a92a189a4309dd15f`；索引SHA `fbaf9db8c1c46329300bd76deec0309b9ff4ad48471c662c5c00c33a47796ce1`。候选ZIP SHA `e4398e4bdba8d8d0995390fcaeb8d131155d596a5566c9c256952a19b2930932`，主合入原件SHA `39e9e06305cedad704b17eba69304dc8b9d2d55bc9449e49582316ce0aae29b9`。

artifact构建与主仓构建的generated PDB路径不同，不声称DLL/PDB逐字节相同。两边均独立核对源码；五个程序集的方法IL、局部签名、异常区间、实际静态字段数据和仅规范化MVID/字段映像地址后的完整metadata相同。原逐字节比较及仅去MVID的诊断不足被保留，最终范围见`main-symbols-r1/logic-comparison-r2.json`。

使用[Test-StartInitialization.ps1](../../../scripts/Test-StartInitialization.ps1)。所有`first-*`模式单独使用新固定owned夹具；此前任模式单独使用既有9fe8夹具。混合批次和错误夹具在执行前拒绝；重编recipe不会自动取得任一固定身份。runner改动不影响已执行的28套编译项目，另外实际验证first-hold22、旧hold61及错误夹具拒绝。

此前使用旧夹具、临时恢复分配模板的7场景256项及无终态first-normal只保留历史范围，不用于验收本增量。新主仓没有安装或启动游戏，也未操作UI或账号。共享MUG构造器、未知lower writer、完整source generation、真实生产首次执行准入、任意Seek的玩法状态重建、effect完成及B矩阵仍待处理；根计数归零不证明原生对象或GPU资源退休。真实Speed/Seek继续关闭。
