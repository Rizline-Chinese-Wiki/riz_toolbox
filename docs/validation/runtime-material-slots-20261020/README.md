# 607 包的 Translucent Float 材质槽实测

新包在真实 LevelList `Canvas/BG` 的 Translucent Graphic 材质槽上完成两次克隆创建及覆盖。第一次手动 Dispose，第二次在离开 LevelList 到 FakeMainMenu 时 Dispose；覆盖租约失效、资产租约失效，探针保留克隆/覆盖/估算字节归零。当前证据只覆盖 Float 部分，不证明 TMP Color、基线材质恢复、可见像素或 GPU 资源全部通过。

采用 docs-generator 的 Evidence → Finding → Path 结构；范围仅本目录冻结包及所选会话，不接触当前游戏。

## 复核

在项目根目录运行：

```powershell
C:\Python314\python.exe docs/validation/runtime-material-slots-20261020/inspect-runtime.py
```

已实际运行通过：**488条单会话连续事件、导出长度/哈希、17个启动前实际安装哈希、24项目607/607离线构建及Visual47/47**。生成 [runtime-proof.json](runtime-proof.json)，含每条证据的序号、时间和源文件行号。会话 `20261020T103706806Z-674f077f11744d8bb9ca694bbe0ee0d6`，范围2026-10-20 18:37:07–18:41:10（UTC+8；准确首尾时间见摘要）。

输入为 [导出清单](session/export-manifest.json)、[包清单](package-manifest.json)、[安装检查](installation-check.json)、[冻结状态](status-material.json)、[Player日志](Player-material.log)、[构建摘要](build-summary.json)。607与603的Adapter及包哈希不同，差异逐文件写入摘要；不能把603的Back/Skip实测或582的Pause/Retry实测记为此607包重新通过。

## Evidence → Finding

| ID | 事件序号 | 可以证明的范围 |
|---|---|---|
| E-001 / F-001 | 190、373–376 | 首次操作仍持有旧MainMenuCanvas/BG选择，TARGET MISSING、code20；没有把旧句柄自动换成新对象。 |
| E-002 / F-002 | 383–384 | 刷新后选择真实LevelList `Canvas/BG`，描述明确 `slot=graphic; source=unity.ui.translucent-image.material`，规范键含 `/slot/graphic/binding/`。 |
| E-003 / F-003 | 394–398 | `_Brightness=0.7`、`_Vibrancy=1.1`、`_Flatten=0.1` 请求后，CREATED有效，APPLIED活跃；探针保留1个克隆、1个覆盖、估算65536字节。仅Float请求，colors为空。 |
| E-004 / F-004 | 413–417 | manual恢复请求后覆盖 `activeAfterDispose=false`；对应资产 `managedLeaseValidAfterDispose=false`，保留克隆/覆盖/估算字节归零。RESOURCE COUNTS的assetValid是null，因为探针已丢弃引用；不能把null写成原生对象销毁证明。 |
| E-005 / F-005 | 431–435、445–450 | 第二次使用不同资产及覆盖ID成功创建/应用；随后scene-change清理同一租约，托管资产失效，保留计数归零。实际场景为LevelList→FakeMainMenu。 |

`CreateMaterial` 成功代表生产适配器接受每个原生 SetFloat/GetFloat 的有限值及容差读回；`Override` 成功代表通道赋值读取被接受。冻结日志记录成功凭据，没有公开原始GetFloat数值转储。请求没有Color，不能借探针通用文字里的“GetColor/GetFloat”宣称Color已实测。

两次Dispose后累积created/released/applied/restoreRequests均为2，当前保留clone/override/估算bytes为0。探针计数不等于GPU显存、Unity原生对象统计或全进程资源数。SDK没有公开基线恢复receipt，RESTORE REQUESTED也明确写“baseline readback not exposed”；零Error不代表基线恢复和渲染全验收。

## Path与剩余工作

P-001：旧选择拒绝（E-001）→刷新真实Translucent槽（E-002）→严格来源校验及克隆参数写入读回→通道赋值→manual Dispose及托管租约释放（E-003/E-004）。

P-002：同一当前槽再次创建不同克隆→覆盖活跃→场景变化→Dispose与释放→保留计数归零（E-005）。此路径证明所测清理请求和托管失效，不补造可见像素/GPU或基线读回。

TMP现在有独立 `fontSharedMaterial` 语义槽设计，但本会话没有成功TMP Color请求。发现仍有128目标上限；新发现排序并不能解决已满绑定集对后来TMP/其他目标的动态饥饿。仍需动态准入、逐类双插件仲裁、失败保留、独立基线恢复/像素、重复资源压力和音符/轨道/相机/后处理语义目标，B13保持未勾选。
