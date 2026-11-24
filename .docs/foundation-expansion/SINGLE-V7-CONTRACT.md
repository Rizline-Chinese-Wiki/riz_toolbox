# Single 事件正式接线：当前 V7 合同

本页冻结本轮跨 native / CLR / package 的内部布局。完整 B01–B19 继续施工；该合同及自有 fixture 不能发行真实游戏 Ready。

采用 `TWEEN-SINGLE-MID-NEXT.md` 第 8 节的 416B section，字段、偏移、七 span 顺序及大小不变。外层当前包为 `RizFirstInstallInfoV7` / `NativeFirstInstallInformationV7`，原 operation matrix 的 2112B 前缀后追加 `singleEvent` / `SingleEvent`，总大小 **2528B**，顶层 ABI **7**。仅导出 `RizFirstInstall_GetInfoV7`，不再导出 V6 getter。operation row 的物理布局仍为 64B，26 rows，operation capability version 仍为 4；当前写入总数 **64**，source count **22**，helper export count **83**。

section 的 phase 采用外层 FirstInstall phase 枚举；capabilityVersion=1，byteSize=416，operationId=25，kind=3，slot=31，expectedWrites=7。epoch 是 Single producer 自己的实际配置代次，parentEpoch 必须等于 operation row 25 的实际 epoch。configuredMask 只记录七段各自准备和封印完成的事实，publishedMask 只记录对应源 span 的实际成功写入；两者仅使用低七位。attemptedWrites / completedWrites 记录真实调用，不从期望数推导，正常最终值均为 7。外层 writesStarted 正常最终值为 64。

三个入口和六个现有 producer export 与 P1/P2 相同。配置与 publication 保持 helper 私有；CLR 只读此事实，不能通过复制的 DTO、bool 或调用 Configure 构造授权。完整性复核覆盖 allocation、代码、所有 xdata/RF、七段正文与保护；任意依赖变化使当前 owner 永久 Unknown。改写前仍验证原 647B / 四 RF 以及现有固定游戏指纹。

CLR 同步接收必须先对 native `ReadCurrent` 得到的当前 frame 里的所有将使用的对象取得实际根，再调用 IL2CPP 元数据或对象访问。parent / child 的代次、native sequence、managed token、原始 float bits 和精确三 site ABI 都独立核对；void listener 的寄存器解释不能复用。只完成生产发布不等于已经完成任意 listener effects 的归属、转移和退出。

native lane 与 source / references / retirement 的在途责任一并处理，异常和原函数必须保持；首次安装部分失败永久保留实际责任，不清理或复用来消除 Unknown。已有 P1/P2 失败证据和冻结结果保留。
