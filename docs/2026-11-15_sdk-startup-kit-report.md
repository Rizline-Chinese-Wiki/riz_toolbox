# 当前SDK开发包交付验证

已提供当前1172候选的SDK1.2.0开发包，并从新ZIP实际解压完成四条文档命令：隔离模板安装、生成MyMod、本地feed恢复、Release编译。四个原始child Popen.wait均0，controller32812的原exec session11344完成实际0/f13ea9；Release为0警告/0错误。编译产物未复制任何RizToolbox宿主DLL。

本包用于开始编写插件，包含精确SDK引用nupkg、5份当前模板、29份合同参考源码与API索引、4个固定.NET6引用/host nupkg及.NET8 global.json。它不含宿主运行时或游戏，SDK/API和支持范围以当前候选为准，不承诺旧二进制兼容。完整SDK/B01–B19、真实Speed/Seek、LOCAL engine生产准入和实际主目录整合仍未完成。

| Evidence | 事实与身份 | Finding / Path |
|---|---|---|
| E-01 [实际交付ZIP](../artifacts/delivery-20261115/RizToolbox-SDK-1.2.0-preview-20261115.zip) | 18,484,558B，50成员，SHA 3dc92a00099da0c55276defe963826597d4c43acaa0ff89a97f066d44f212817 | F-01：当前SDK和模板可交付；design / validated / high；P-01 |
| E-02 [实际四步结果](validation/sdk-startup-kit-20261115/run/result.json) | SHA 4afeb847735ef495436f7fb0ee63b2c6fbbad6156ec580d0e3b6fce51929b6bf；新hive和cache，准确恢复SDK7932f577…cfa1 | F-02：文档里的新建插件流程已执行；design / validated / high；P-01 |
| E-03 [生成插件](validation/sdk-startup-kit-20261115/generated/MyMod.dll) | SHA 3d902c51a4cadc573b61f544957862f65a955914b120345342c1618aef4ebc95；author.mymod / MyMod.Plugin，Release W0/E0 | F-02的直接产物；P-01 |
| E-04 [证据清单](validation/sdk-startup-kit-20261115/validation-summary.json) | SHA 7a53348ba5e3ba309db81c4d19078db538c74a0395b6f985de70c0cb4301d121；原日志、控制、生成源、恢复nupkg和原工具退出观察 | F-01/F-02可按交付ZIP与证据两项独立核对；P-01 |

P-01（callflow）：已接受的同1172图SDK14包/模板→核对原四依赖byte pins→封装新SDK开发ZIP（E-01/F-01）→实际解压并按README四命令执行→恢复的nupkg与交付精确一致→生成插件W0/E0且不带宿主DLL（E-02/E-03/F-02）→原exec session自然0→只读归档及交付ZIP检查（E-04）。原SDK14有[自己的证据](2026-11-15_sdk-startup-candidate-report.md)，没有拿旧939包代验。

原创建receipt中的deliveredFlowSmokePassed=false记录创建当时尚未执行smoke，保持原字节；[新接受记录](../artifacts/delivery-20261115/sdk-preview-validation.json)绑定随后实际通过，未改写旧ZIP或旧记录。原SDK14证据不含依赖byte的限制不变；本交付另从其固定本地feed核验后补入四包，能够执行这里已验证的本地恢复流程，仍以已安装.NET8 SDK为前提。

只读复核明确传入两项文件，不读取原D盘来源、不运行构建或游戏：

```powershell
python -B docs/validation/sdk-startup-kit-20261115/controls/publish_smoke_evidence.py --check-only docs/validation/sdk-startup-kit-20261115 --delivery artifacts/delivery-20261115/RizToolbox-SDK-1.2.0-preview-20261115.zip
```

发布实际0/9907ea，root另一次直接checker实际0/e1b93f。后台CLI隐藏，只有既定五个dotnet变量在子进程覆盖；NuGet缓存通过--packages传入，没有修改永久环境、全局模板、游戏、账号或Git。此处只接受开发包交付及四步流程，不签完整SDK通过。
