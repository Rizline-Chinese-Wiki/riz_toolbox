# Scope

- auth: 用户授权完成 PC MOD 兼容底层；本子任务由主任务明确分派为本机游戏/既有 dump 的只读最小静态提取。
- in_scope: 固定 SHA-256 的 GameAssembly.dll、dump.cs、script.json；LevelMetadata/LevelDifference/结果转换、紧邻保存/成就与重试消费者；本证据目录。
- out_of_scope: src/tests 变更、构建、启动游戏、UI、注入、调用保存/Steam API、修改原始游戏、网络访问。
- network_profile: offline-only。
- sample: D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll。
- expected_sha256: 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79。
- tool_strategy: 已读 ida-reverse 与 docs-generator；独立安装 skill 的相对 tool-index/precedent/ops 缺失，使用已存在的 reverse-skill-router 同名资源；工具索引标 IDA/r2 不可用。本任务按主任务指定复用既有 pefile/capstone 脚本路径，不安装额外工具。
- completion: 完成有界静态判断及可复现证据；不将此子任务完成当成 B15 或原生播放验收。
