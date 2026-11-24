# 存储、语言、共享服务与数据的实际游戏验证

游戏为 Steam build 25977441。包文件核对使用 [本次清单](package-manifest-corrected.json)，17 个文件均与安装内容一致。本次启用的是 SDK-only 集成探针，配置 `foundation-proof.Enabled=true`；普通安装默认关闭示例和自退出验证。

| Evidence | Finding | Path |
|---|---|---|
| [会话](runtime-session/events-000001.jsonl) seq20/51 | 两个独立包各自完成 schema1→2 迁移，并保留迁移原件；同名 Data/Cache 文件属于各自插件 | 插件存储实际读写路径通过；配额/链接拒绝等另见 Storage.Tests，游戏设置尚待其他模块 |
| 同会话 seq21/52/54、seq63/64 | 相同语言键得到各插件自己的值，运行中切到 zh-CN 后双方更新 | 插件语言命名空间及热切通过；不等于游戏语言设置已实现 |
| 同会话 seq22/53/55/56/66 | 消费者先收到未就绪，提供者发布后跨私有 ALC 调用成功；停用提供者后消费者收到不可用 | 实际两包共享 SDK 契约、可选服务退化通过；提供者故障和硬依赖联动还有独立流程待验 |
| 同会话 seq58/60 | 两包各自读完444谱面与444成绩，各页同 session/revision，取消请求被拒绝 | B10实际分页一致性通过；当前局结果及游戏设置在此包仍为 Unsupported |
| [停用状态](provider-disabled-status.json)、[启用意愿](activation-disabled.json) | 提供者停用后 DesiredEnabled=false，消费者继续 Enabled | 落盘意愿存在；跨重启恢复需下一次成功宿主启动验证 |
| [拒绝的迁移](probe-migration-rejected-session/events-000001.jsonl) | 探针迁移函数输出字段大小写错误，被校验拒绝，原 schema1 文件保留 | 修正探针输出为契约规定的 camelCase 后实际迁移通过；失败证据保留 |
| [重启会话](restart-before-host-session/events-000001.jsonl)、[Unity日志](restart-before-host-player.log) | 启动桥安装后游戏在宿主初始化前退出，未取得新 status | 不将旧 status 当新重启证据；该次不能证明持久启用恢复，也未据此判定是框架或游戏退出原因 |

原始游戏二进制 SHA-256 在本轮开始时与已知基线相符：Rizline.exe `f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e`，GameAssembly.dll `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。探针 DLL 修正仅通过正式安装脚本更新归属包。

重新执行：完整构建并安装，显式启用 example.events/example.scores，在两个插件各自 `Configuration` 的 `foundation-proof` 中设 Enabled=true；启动游戏，核对 `FoundationProbe` 日志，再通过宿主 `command.json` 执行 set-language 和 disable 提供者。完整任务验收仍以 [SPEC](../../../.docs/foundation-expansion/SPEC.md) 为准，不能以本次子集结果勾完 B04/B10/B16/B17。
