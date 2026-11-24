# 研究范围

用户已授权为本机 PC Rizline 实现并实测 Mod 兼容底层；本次子任务仅检查主动退出与重试的生命周期语义。读取固定版本 GameAssembly、既有 dump、当前仓库生命周期与 hook 实现；仅写入本证据目录，不改生产代码、不构建、不操作游戏。IDA 不可用，使用已存在的 Python、pefile、Capstone。结论只适用于 SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。
