# TMP 文字颜色接入与验证

2026-10-21。现有游戏颜色目标服务已扩展到 `TMPro.TMP_Text.color`，复用共享语义目标、代次和优先级仲裁。GameResources 专项 **25/25** 通过，含7项新增元数据/服务用例；尚无本包真实 TMP 对象读回或恢复证据，B13 未验收。

用户授权范围为本地 PC Mod 公共视觉底层。原生类型/方法来源、dump 行号/RVA、所有权和验收边界见 [设计与静态来源](DESIGN.md)。本批没有启动游戏或修改配置。Image 使用精确 `Graphic` 声明的访问器；TMP 只接受精确 `TMP_Text` 声明，拒绝回退到 Graphic。TMP 符号缺失只排除 TMP，不撤回 Image。字体共享材质与颜色字段仍为独立通道。

| 证据 | 支持的结论 | 限制 |
| --- | --- | --- |
| DESIGN.md 中 dump 120378/120379 | 已知构建声明 TMP_Text.color 的精确签名与 RVA | 静态声明，不是实际调用记录 |
| game-resources-tests.log | 25/25；访问器签名拒绝、类型元数据、双插件覆盖、独立基线、别名共用代次、失败保留、销毁/重建/不活跃恢复 | 元数据 seam 与托管假目标，非真实 TMP/GPU |
| 本轮整合包的 source-manifest 与 build.log | 纳入正式 Loader、兼容性和整包检查 | 不自动继承历史游戏证据 |

发现路径：共享4096预算内轮换扫描 Image/TMP → 活跃、加载场景和框架根排除 → pointer/实例身份去重 → 原有 SemanticTargetRegistry → 原有 MutationCoordinator 的 color 属性。规范键附绑定代次；`Role=tmp-text-color`、`ResourceId=unity.tmp-text.color`，会话别名 `game.ui/tmp-text/{instanceId}/color`。名字只供描述，不用同名取第一对象。颜色缓存仍有512目标上限，容量满后不能宣称每个活跃对象都已暴露。

复验（仓库根目录）：

```powershell
dotnet run --project tests/RizToolbox.GameResources.Tests/RizToolbox.GameResources.Tests.csproj -c Release
```

后续仍须真实 TMP 前后 getter、两个插件覆盖/释放顺序、游戏写新基线、停用/切场景和最终独立读回，不能由假目标回归推导像素/GPU或物理释放。原颜色通道的恢复失败边界与全目标动态准入也继续遵循未完成 B08/B12/B13 验收。
