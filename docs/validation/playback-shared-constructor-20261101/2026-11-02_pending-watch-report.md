# 构造器交接前的同值与 ABA 修复

当前修复位于独立候选，尚未合主。未安装或启动游戏，未执行游戏方法、游戏 UI 或账号操作。生产 Speed/Seek 仍关闭。

## 已确认的问题

新的播放器已经被实际 source owner 的独立根保留并完成 watch Stage，但 source-open 尚未执行时，旧实现仅识别 active watch。此时实际构造器可改写新播放器的 source 字段，然后改回原值；最终图读取相同，陈旧交接仍可能通过。

修复前自有 native 反例实际 exit 1：175 项检查中 2 项失败。三次实际构造器调用均未推进 source fence，随后 action10 仍接受该 capture。原件保留在：

`D:/riz-build-cache-next/playback-constructor-integration-r1-20261102/audit-pending-before-open-r1/test-result.json`

## 修复行为

在同一 owner 锁内先匹配 active phase2，再匹配 pending phase1 的确切 player。只有匹配后才读取其 class；foreign receiver 不产生 playback token、journal、fence 或 worker fault。

pending player 的构造尝试沿既有 Enter 路径推进 source fence，使普通交接 action4 和首次捕获 action10 拒绝旧 capture。关闭的逻辑 gate 继续只产生 Dormant 观察，不向托管层发行构造票。相关 worker 和 class 失配保留 Unknown；没有尝试的正常交接及 foreign 调用仍能打开。

Source V4 的 168B frame／136B info、Writer V1、Init V2、FirstCapture V1 及正交 PlayerWatch V1／104B 均未改变。

## 实际验证与源码范围

修复后的 native build 实际 exit 0。22 个独立进程、4144 项检查全部通过，其中新增12个场景1560项，分别覆盖普通／首次交接中的同值、ABA、foreign、worker、class 和正常路径。该证据直接调用自有 native helper，不包含 Dobby 安装或 SDK 执行。

原件为 `pending-fix-r1/test-result.json`，两文件补丁及13份编译输入在同目录；精确纳入候选的哈希和改前备份在 `pending-canonical-apply-r1/`。最终 Source helper SHA256 为 `7f2d78402d42cb15636afc3b60f0f26acd1b0ed93a64d9034de64725bed8f27c`。

新 `checks-r3` 冻结866个源输入，三专项构建均零警告／零错误。`symbols-r3/test-result.json` 实际核对15对PE/PDB与1487份文档。相较旧869快照，删除的3份未引用草稿已在独立目录备份；托管产品源480输入未变，另修正异常测试的断言，使其核对既有异常撤权与七根保留合同。

新helper下原8role的14进程36675项和13成员fixed-image joint33项已实际通过，见 `native-mapped-r3/test-result.json`；映像游戏方法／DllMain／启动均为零。新helper下根回收／首次hold／首次异常4进程78项亦实际通过，见 `quick-r4/test-result.json`。

18项正式SDK／source-open／first-normal／worker专项仍须以各自实际终态为准。旧中断记录保留；`sdk-continuation-r4`从现成866冻结程序集和新helper续缺项，不重编、不使用旧helper替新修复签字。

## 交付边界

本修复补的是已 Stage、尚未 source-open 的构造窗口，不证明完整 source generation 或 Unity 内部未知写入。直接 AudioSource 六入口的独立副本与本866冻结分开。生产首次准入、任意 Seek 的玩法状态重建、effect 完成和 B01–B19 仍未完成。
