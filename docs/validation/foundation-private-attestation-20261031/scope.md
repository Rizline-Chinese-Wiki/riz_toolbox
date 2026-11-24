# 私有 FirstInstall attestation 证据审查范围

- `auth`：用户已授权继续 riz_toolbox 底层功能实现与验证；本项由父任务委派，限定为证据收尾和只读实质审查。
- `in_scope`：当前 FirstInstall 私有 pre-Prepare attestation、CLR opaque membership、共同组组合代码的只读审查；核对四组最终实际结果；PE/PDB/COFF/源归档与 core-r3 相关源哈希；本目录报告与完整索引。
- `out_of_scope`：修改冻结生产代码或根规范、commit/reset、候选打包/安装、游戏启动、游戏 UI、账号、真实游戏原方法/DllMain/il2cpp_init 调用、production 首次写入准入。
- `network_profile`：offline；没有网络请求。
- `flavor`：null；本项是自有代码和本地产物的工程验证，不涉及 malware/APT、IOC 或 ATT&CK 归属。
- 当前目录：`C:/Users/71957/Desktop/riz_toolbox`；全部新验证输出使用未存在的 D 盘目录。

授权和功能边界沿用 [N2 合同](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md) 与 [共同整合范围](../playback-nine-source-integration-20261031/scope.md)。本报告只对证据索引列出的精确源和产物负责。core-r3 在这里仅核源哈希；它的完整执行结果由父任务报告。

已存在的目录名是 `foundation-private-attestation-20261031`，因此沿用本目录，没有另建同义的 `foundation-private-playback-*` 目录。
