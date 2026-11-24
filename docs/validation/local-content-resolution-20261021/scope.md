# B15 本地游戏内容解析边界

2026-10-21。来源为主代理传达的用户 PC Mod 底层授权与指定 GameAssembly/dump。只读静态，约10分钟；游戏关闭，不启动、不附加、不发网络请求、不修改原版文件。输出限本验证目录，不改其他代理负责的 LocalContentService、AudioContentContracts、AudioContentModules、AudioContent.Tests。

采用 reverse-engineering 技能。已读取 SKILL.md、re-agent-workflow.md，修正旧引用到 reverse-skill-router/field-journal/precedent-reverse.md 与 tool-index.md；工具实际路径 C:\Python314\python.exe。Triage→Static→Synthesis；用户省电要求覆盖默认 Dynamic 步骤，本次明确不进行运行验收。

假设：现有真实 Workshop 接口包含曲谱、目录/音频和封面生命期，不能仅凭 LoadChart 替换成功宣称完整本地 provider。目标是用固定镜像证据缩小最小接入设计；若 native 对象构造、异步移交或释放证据不足，保持 unsupported。
