# Scope

- auth: 用户已授权完成 PC MOD 兼容底层；父任务分派本次为只读 LOCAL 启动/结算/重试设计与必要静态提取。
- in_scope: 固定 GameAssembly.dll、既有 dump/script.json、既有 evidence 与 Runtime.Hooks 源码；仅本证据目录新增文件。
- out_of_scope: src/tests 修改、构建、UI、游戏执行、注入、保存/Steam API、游戏文件修改、网络。
- network_profile: offline-only。
- expected_sha256: 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79。
- tools: 已读 ida-reverse；独立 skill 相对 precedent/tool-index 缺失，读取 reverse-skill-router 同名资源。工具索引 IDA/r2 不可用；按父任务复用 Python/pefile/capstone 静态提取，不安装工具。
- completion: 只完成有限静态设计证据，不完成 B15 或原生播放验收。
