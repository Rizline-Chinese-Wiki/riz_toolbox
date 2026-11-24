# 数组协变准入失败的原始记录

两次会话均已从菜单正常退出，并由进程查询确认 Rizline 不在运行后复制完整 events/session。它们是失败证据，不能交给成功验收脚本或计入 inactive clone 成功。

| 会话 | 包 | 实际结果 |
|---|---|---|
| `20261022T103115700Z-0e896a8528fb4124beed23b79a23f4d6` | 首个 951 检查包，清单保存在 `../../foundation-inactive-controller-20261022/before-array-diagnostic/` | seq100 拒绝 Resources 数组的精确类型；尚未选择 prefab 或创建 controller |
| `20261022T103449360Z-40972f01dff0469fa5c8f061c13ca03d` | 只补诊断的中间包，未重跑完整检查；见 `../../foundation-inactive-controller-20261022/array-diagnostic-package-manifest.json` | seq100 记录 `source=LevelCardControl; actualType=LevelCardControl[]; rank=1`，仍拒绝，尚未创建 controller |

E-array：第二次完整事件中的实际类型/维数。F-covariance：`Resources.FindObjectsOfTypeAll(Type)` 的声明返回类型为 `UnityEngine.Object[]`，本游戏实现返回请求卡片类型的协变数组；原准入把合法返回拒绝。P-fix：补充接受声明 Object[] 或当前来源的精确 card[]，仍要求 rank1、总数≤4096，以及每一个非空元素的精确卡片 class、活性/场景和 prefab 字段。错误来源、无关数组和 rank2 由新增两条回归拒绝。

这里的 Error 原样保留。后续新会话才可验证修复，不改写这两次结果；本目录与新完整构建/实测分别保留。
