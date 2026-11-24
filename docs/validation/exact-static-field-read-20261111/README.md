# 精确静态值字段读取：2026-11-11

**本批只完成Runtime精确静态值观察前置，不代表actual Canvas/down/right源捕获或目标环境已重建。**

`ReadExactStaticField<T>(NativeExactReadField)` 首先拒绝错误线程，再重新检查declaring/static/type/offset/size/flags及保存descriptor的完整相等性。reference字段在通用ReadField前拒绝，不能借IntPtr/UIntPtr绕过publication和root责任；真正System.IntPtr值保持NativeAbi原有合法范围。InitOnly静态字段可观察，Literal与非静态访问拒绝。

通用ReadField先执行NativeAbi布局检查，再分配准确输出buffer并调用official `il2cpp_field_static_get_value`。无实例receiver、root、字段写入或Unity方法执行的声明。

## 真实验收

- 独立新增 `--exact-static-field-read-checks` **6/6通过**；旧`--renderer-abi-checks`组合未改，仍 **51/51**。
- 新六组件与判定构建W0/E0，判定 **252/252**；正确自有native环境中的完整AudioContent默认 **1001/1001**。
- **799**输入执行前后不变，**24**DLL/PDB副本匹配本次六图。Runtime既有CS0649及AudioContent现有测试警告保留在原日志，不伪报全零警告。
- [短路径源码复核](source-review.json)完成，没有阻断问题；执行与source审查分开，[真实结果](build-test-result.json)记录完整argv/退出码/哈希。

六组测试涵盖Single +/-0/subnormal/NaN payload/Infinity原始位、一字节Bool及邻接guard，现有NativeVector3准确三槽独立拷贝，11种保存descriptor/17种当前元数据漂移（含布局缓存预热），managed载体错类型/宽度、readonly观察、worker零export/lazy lookup及main后续有效，以及reference零getter/root。

[RED](red-build.log)明确缺失方法，后续[first GREEN](first-green-checks.log)及新图都真实执行，不使用静态源码替代6/6。

## 后续

actual Canvas target source还需独立root的LevelInformation/CanvasControl、CanvasMove/keypoint完整数组及List、已初始化EaseInfos、Down/Right官方静态向量值与身份、准确range converter和跨callback来源检查，再与纯数值plan组合。未知初始化/consumer/Source closure不签完整Seek。

未启动/安装游戏、未修改代理/账号/全局PATH/旧ZIP、未提交推送。此API的测试不代表真实游戏static getter及隐藏初始化副作用已验证；生产使用仍受构建/生命周期准入约束。全B01–B19目标继续。
