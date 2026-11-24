# 范围与授权

- auth：用户要求实现桌面 `riz_toolbox` 的 Rizline PC MOD 兼容底层，并允许多代理分工。当前人工指令是“先继续代码，实测稍后”；本子任务执行离线读取和项目代码/文档编辑。
- in_scope：固定 `GameAssembly.dll` 七个列明 native 范围、同版本 dump/script、构建场景名，以及首次 LOCAL 转场/激活后的对象生命周期核验。
- network_profile：offline；没有网络请求。
- out_of_scope：本次不操作游戏 UI、不启动/终止游戏、不安装新包、不改游戏文件、不执行保存/Steam 调用、不验证完整相机/输入平衡或活动局退休。
- completion_boundary：仅完成有界静态取证及实现交接。全目标 B01–B19、用户启动闭环和游戏实测均未据此完成。

时间线（2026-10-23）：先发现 controller 激活与 LevelControl.Start 所属场景不同；再读取菜单/Workshop 转场及短回调范围；最后冻结证据、实现真实异步完成凭据，并按已完成同局转场区分暂存对象与持久 clone 的活性要求。
