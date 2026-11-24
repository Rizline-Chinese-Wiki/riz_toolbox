# 下一离线工作：同源 Canvas basis 组合，再接 line/ring

位置命令与计算缓存各自已有正式实现；缓存确认不能清除位置命令的 Unity 外部效果。两者独立捕获，尚未证明为同一次目标环境发布。

1. 内部 Canvas basis owner 共享同一次独立 NativeSeekCanvasTargetCapture 的 Plan、完整 Transform 映射与缓存目标，全部子命令使用该来源；不重新推导或调用 producer。准备读取完整两类 baseline、别名与 exact metadata，零写。
2. 依照已冻结 GetInformations / SetPosition / Update 的实际依赖，确定缓存、time/speed、位置的一次执行及完整读回协议。相同 source profile 不等于同一次采样；所有 partial native、来源过期、回调撤销都保留已进入子 owner。
3. cache 值可条件恢复；位置 setter 进入后其 journal、来源与根仍由最初公共 owner 持有，aggregate Dispose 不能擦除引擎责任。第三态不覆盖，不制造完整回滚。
4. 真实 backend TDD 覆盖同 Plan、空图、多别名、跨子过程失败、RunValue 退出、撤销、原位恢复、旧确认失效、原 owner identity 与未知 free。只用本批新源码产物验收。
5. 组合不签 Unity callback/consumer closure。然后继续 line/ring target basis、缺失 runtime、renderer 延后 consumer/source、Hold 引擎退出，再组合完整时钟/Seek 事务。

B01–B19 未整体完成，真实 Speed/Seek 关闭；不安装或启动游戏，不操作 UI/账号，不重复相机、N1、退休及已冻结 Play/clock 批次。
