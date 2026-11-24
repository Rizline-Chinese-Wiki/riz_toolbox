# StockStopV1 独立审查范围

2026-11-02。授权来自主代理本轮任务：只读审查 D 盘候选源码；后续明确授权在独立 D 盘目录复制并构建自有 native fixture、执行最小反例与 n3 修复复验。此范围不扩大到游戏、UI、账号、安装或启动游戏。

- auth: 本会话主代理指派；先只读审查，发现候选后追加独立自有 fixture 构建和执行授权。
- in_scope: `D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/source` 的 StockStopV1；native-r2/native-r3；本目录的自有反例与回归。
- read_only_target: `D:/riz-build-cache-next/playback-direct-audio-managed-r1-20261102/mapped-r4/image/GameAssembly.dll`，只解析 PE/字节，未加载执行。
- write_scope: 当前独立审查目录；不修改 T/source 或主工作目录。
- network_profile: none；没有网络访问、账号、UI或游戏执行。
- excluded: production admission、完整 sourcegeneration、未知 lower、Seek/effect、B01–B19、真实游戏运行。
