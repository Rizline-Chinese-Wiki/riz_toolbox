# 范围

- auth: 用户要求完成 PC MOD 兼容底层；主任务分派本机现有游戏、逆向 output 的只读初始化顺序补证。
- in_scope: 固定 SHA 的 GameAssembly.dll、dump.cs、script.json；九个明确函数范围；Level 场景的三个 MonoBehaviour、对应 MonoScript 和一个 GameObject；本目录文档及验证器。
- out_of_scope: 游戏/进程/GUI 操作、安装、构建、原生方法调用、源码修改、保存/Steam 调用、全量资产导出和网络访问。
- network_profile: offline-only。
- tool_strategy: 已读 ida-reverse、docs-generator、reverse-skill-router 中 tool-index/precedent/evidence 契约。索引标 IDA/r2 不可用，按主任务明确要求复用既有 pefile/capstone 有界提取器；现有 UnityPy 仅读取两份小文件。
- sample: D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll。
- expected_sha256: 853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79。
- completion: 核实有限调用路径、原始序列化引用和新增 Steam 初始化调用；不作为 LOCAL 启动或原生隔离验收。
