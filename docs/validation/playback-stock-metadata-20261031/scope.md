# N2 stock metadata 与固定正文范围

- auth: 用户当前 N2 S1/S2 离线实现授权，沿用 Sol/max 代理分工；本包实现 resolver/profile、独立 metadata 测试与真实私有共同组的固定 image 联合验证。
- in_scope: 实际 IL2CPP metadata 声明检查代码；固定 GameAssembly SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` 的只读复制、14 个已确定正文边界的 hash、仅 `AudioSource.set_clip` 一段新反汇编；D 盘副本以 `DONT_RESOLVE_DLL_REFERENCES` 映射与 synthetic metadata fixture；仅在该进程副本中执行实际 Dobby Prepare/Commit 安装 GetTime/SetPitch 两个 hook，验证 profile、ClockPublication 1→2→3 与恢复正文后仍撤回。
- out_of_scope: 游戏安装/启动/UI/账号、游戏方法执行、生产首次安装资格、底层 icall 完整 writer 覆盖、任意 Seek、跨局 logical handoff、旧测试包重新验收。
- network_profile: offline；不搜索网络、不下载或安装工具。
- source_ownership: `NativeRuntime.PlaybackHooks.cs`、`NativePlaybackStockCodeProfile.cs`、`NativePlaybackStockMetadataChecks.cs`、`NativePlaybackStockGroupProfileChecks.cs`、`scripts/Test-PlaybackStockJointProfile.ps1` 和本报告目录。共享 enum/lease/group、SingleReturn bridge、旧 clock profile 与 Program 注册由 root/group 所有。
- evidence_root: 静态 `D:/riz-build-cache-next/playback-stock-metadata-r1-20261031/`；历史 metadata build-r1、joint-profile-r1 与后续新的 joint-profile 目录；新建前确认不存在，保留全部执行产物与失败，不覆盖历史目录。

metadata receipt、固定 profile 和独立 fixture 均不能签发生产支持。默认真实 provider 继续关闭。
