# 相机横向缩放：已闭合的离线候选

新增 `GameCameraRoles.RigLocalScaleX`／`local-scale-x`，普通SDK插件可以对当前局rig的X轴缩放单独覆盖、仲裁和恢复。每次读取最新Vector3后只改X，保留Y/Z；位置与缩放各自持有完整图根和独立通道。

[完整记录与源码](offline-r1/2026-11-03_game-camera-scale-x-report.md)已经保留到本桌面项目，候选产品尚未合主。当前通过范围：专项55/55、整树28套2620/2620、SDK独立双插件14/14、solution0错误／56既有警告、六组件Release、11对PE/PDB与990份源码文档。固定4方法848字节只读核对通过；未运行游戏。

归档含763份原件、全部915输入源码ZIP、9路径差分和实际构建／测试日志；[scope-index](offline-r1/scope-index.json)明确 `mainApplied=false`、`runtimeCameraAcceptance=false`、`fullBCompleted=false`。归档后另核769份文件及ZIP915条目，0issue。

下一步仍是收齐U末SDK与独立审计、T剩余六Runtime场景，U先精确合主，再只合该候选9路径。不能整包覆盖U已验证的Lifecycle Dispose修补。真实Update/render次序、持续可见效果、投影和玩法影响另待实测；当前不安装或启动游戏。
