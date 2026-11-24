# 本次离线范围

- owner: riz_toolbox 项目用户
- authorization: 用户要求自主推进底层功能、离线构建测试并更新证据与checkpoint；本轮“继续”沿用。
- allowed: 隔离候选源码修改、固定游戏文件的只读静态分析、自有fake/exports正向、整树与SDK离线验证、桌面证据归档。
- forbidden: 真实游戏安装、启动、方法或DllMain执行、游戏UI、账号操作；无commit/push/reset。
- candidate: D:/riz-build-cache-next/game-camera-scale-x-r1-20261103
- baseline: U913／484managed／48native；新915／485managed／48native。
- acceptance: 本包只签local-scale-x的离线增量；mainApplied=false、runtimeCameraAcceptance=false、fullBCompleted=false。
- integration gate: 等U与T真实闭合、U先合主，再只合V的9路径差分；保留U已经测试的Lifecycle Dispose修补。
