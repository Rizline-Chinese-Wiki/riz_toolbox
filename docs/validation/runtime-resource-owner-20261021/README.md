# 原版资源引用实际派发

包：`../foundation-resource-owner-20261021/`，793/793、25套回归、0编译警告/错误，18安装hash与270源码输入一致。游戏session `20261021T103006149Z-fb954c63ee9b4865b90dac3cbc1a23c9`，正常退出后全部1335条日志连续、0Error，冻结于 `assetref-owner-dispatch-20261021T103813Z/`。

| 实际字段 | 已持有对象的真实闭类型 | interface Release | 官方resolver返回的实现 |
|---|---|---|---|
| InsideLevelParams.coverReference | `AssetRefReference<UnityEngine.Sprite>` | IResourceReference<Sprite>，MethodInfo slot1 | AssetRefReference<Sprite>.Release，RVA B115D0，MethodInfo slot5 |
| InsideLevelParams.musicReference | `AssetRefReference<UnityEngine.AudioClip>` | IResourceReference<AudioClip>，MethodInfo slot1 | AssetRefReference<AudioClip>.Release，RVA B115D0，MethodInfo slot5 |

这是从原版已root的InsideLevelParams读取引用、临时保活该引用、调用官方 `il2cpp_object_get_virtual_method` 得到的实际MethodInfo关系。两个闭类有不同class/MethodInfo；相同共享RVA不等于同一闭类型或可任意构造。每个entry都通过固定Unity2022 MethodInfo六项元数据交叉核验及GameAssembly可执行范围验证；没有手读Il2CppClass虚表或调用Release。

`runtime-proof.json`由`analyze-session.py`逐条核验：只有1个existing-owner header、完整10行index/count、两种实际类型/声明类/RVA/slots、无Error/Fatal、同一真实开局与结束RunId。原版Result-origin观察仍正常accepted/associated。该局自然到达结果页，FREE模式下0分、479Miss、Crashed=true、EndReason=Failed；不能把自然走到曲末当成正常成功完成。

它没有证明关闭 `ResourceReference<AudioClip>` 已存在：本局两种对象都是AssetRefReference，`existing.*.concrete`仍明确unknown。旧Sprite ResourceReference的AEF2C0/AEF1F0元数据不应套到这两个AssetRef对象。AssetRef的原版资产管理路径也不能直接用来包装任意本地文件。

没有创建本地native wrapper、没有调用资源getter或Release、没有替换音乐/封面、没有LOCAL播放，也未证明原版最终销毁路径或本地请求的取消/撤回/重试/结果所有权。下一步需取得专用ResourceReference<AudioClip>的可验证闭类/方法与本地资源保活策略，再接ILevelMetadata及InsideLevelParams；不可伪造Workshop ID或复用原曲冒充LOCAL。

已正常退出游戏。唯一临时metadata marker已恢复原Absent并复核；见probe-file-restoration.json。原游戏三个文件before/after哈希一致，完整scope仍B01–B19未完成。
