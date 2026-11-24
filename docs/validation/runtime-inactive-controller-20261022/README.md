# 控制器未激活创建与销毁实测

**953包已通过一次真实 inactive clone 生命周期探针。** 会话 `20261022T103941290Z-8066fab3d0424801ac47156ba495cfd1`，775条完整连续事件，746Info/25Debug/4Warning/0Error；[机器证明](runtime-proof.json)和[技术报告](2026-10-22_逆向-Rizline控制器生命周期-report.md)已保存。

| 事件 | 实际观察 |
|---|---|
| seq185/frame10508 | 完整扫描当前两个来源数组，共1个卡片、1个有效匹配；独立controller/GO/parent。parent inactive、clone activeSelf=true但activeInHierarchy=false；空参数/信息；Awake0；六全局字段与flag未变 |
| seq188/frame10587 | 后续帧原生GC返回，图读回一致；来源仍有效；Awake0、globals未变，发出Destroy请求，7 root仍保留 |
| seq221/frame10706 | 再后帧确认controller/GO/parent/两Transform全部Unity-dead，7个持有root和独立观测roots释放，来源保留 |

真实 globals baseline 为 owner `2639647336800`、controllerSet=false、六个delegate均null。仅证明该基线的读取与不变；不包含非空delegate归属、发布、条件恢复或native消费者结束。当前卡片快照只有普通来源匹配；不宣称Workshop非空场景或后续新增卡片的全局唯一性已验。

4条Warning原样保留：设置页等待、目录加载等待，以及两次VisualTargetCapacity分别暂缓38/256个候选。没有删除Warning或把它们变成成功项。此前数组准入的[两次失败](failed-array-contract/README.md)与本成功会话分开冻结。

游戏由root点击返回、退出及确认，进程已停止，见[退出检查](normal-exit-check.json)；临时marker已恢复原Absent，见[恢复检查](probe-marker-restoration.json)。18项安装hash/303源码输入在收尾匹配，原游戏三文件未变，见[构建证据](../foundation-inactive-controller-20261022/README.md)。

复核冻结会话：

```powershell
python docs/validation/runtime-inactive-controller-20261022/freeze-proof.py docs/validation/runtime-inactive-controller-20261022/20261022T103941290Z-8066fab3d0424801ac47156ba495cfd1
```

脚本检查完整事件、实际身份/帧序、六字段快照、零Awake、清理声明和当前18安装hash，并拒绝覆盖不同的旧冻结文件。正常退出是外部前提，不从日志最后一条推断退出。

本轮没有激活controller、写入params、执行本地游玩、修改目录或调用结果/保存。root计数和Unity失活不等于native heap/GPU归零。整体B01–B19及B15完整曲包仍未验收。
