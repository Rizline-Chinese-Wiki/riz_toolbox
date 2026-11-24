# 实际 Doorstop 加载入口：冷启动接线已验证

后续勘误：本批归档的生成 profile 将枚举类型与缓存字段偏移写反，正确为 type `0x18`、cache `0x20`。实际 Kernel 实现一直正确，冷启动转发测试不使用这两个生成常量；新发布批已修正提取器并保留旧四份原件，见[勘误](../foundation-engine-publication-20261025/history/profile-cache-type-erratum/erratum.json)。本页历史626源ZIP仍保留原始时点，不能当作修正后的当前源码。

2026-10-25。root 实际观察 session80976 终态0。新候选保持旧代理全部82项名称和序号，四个WinHTTP入口使用canonical typed转发；真正DllEntry在CRT/日志前完成初始化。自有Host通过普通依赖加载Importer和该候选，真实TLS/C/C++/DllMain随后观察到Ready。

- [5个冷启动/身份拒绝结果](raw/cases/fixture-result.json)：3正常、2明确拒绝，5240父核验、432调用、198对照；41项输入持只读句柄。
- [原21转发回归](raw/standalone-cases/fixture-result.json)：5653父核验，默认独立7/8导出合同保持。旧默认Doorstop另行构建，82导出与已安装基线一致。
- [正式报告](../../2026-10-25_底层-Doorstop冷启动接线-report.md)、[汇总](summary.json)、[真实终态](terminal.json)、[正式命令](attempts/r3/command-result.json)、[交叉审查](fixture-cross-review.md)。
- [626源码清单](source-final-manifest.json) / [ZIP](source-final-inputs.zip)：`9375dd8195c33adc16ba38897e3715137f2ad59661a59c35343629f7834d757f`；旧613中4项明确修改，13项新增。
- [8二进制](binary-manifest.json) / [ZIP](binaries.zip)：`ce561f35ede26c69365680f190a75750d1ba4c04317dc585c71893d37cc79c9e`；候选与旧默认区分保存，不是安装包替换。
- [585份运行/构建原件](raw-manifest.json) / [ZIP](raw-inputs.zip)：`83169b4c9e4609fdd79d85fa141aab5c15580a6f3cefc22781a4c9c2cf269422`。2780份提取后的xmake工具文件仅存哈希，工具原归档由上游锁文件固定；源代码、编译对象、日志及所有case原件保留。
- [四引擎入口固定资料](research/engine-game-profile.md)、[完整事实JSON](research/engine-game-profile.json)、[下一步生产采用](production-next.md)、[范围](scope.md)。

最初候选编译成功但10个旧ordinal变化，严格核验拒绝后修为ASCII名称序；失败/预检保存在[history](history/initial-ordinal/observation.json)。其他78转发保持既有逻辑，不代表全部82个签名/调用已验收。两种负例仅证明各自部署条件下干净拒绝，没有记录被拒模块路径，不能把它们扩大为模块映射来源证明。

本批未安装或运行游戏；18安装项、4原始文件、23已有包项均hash不变。V4、新核生产采用、对象GC/调度/取消与全资源退休仍待实现，完整B01–B19没有因此勾完。此前25套1617没有在本批重跑。
