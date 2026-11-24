# 返回菜单与跨重启恢复

实际包：`../foundation-return-menu-final-20261021/`，789/789、25套回归、0编译警告/错误。18项安装hash与268源码输入核对一致，原游戏三个文件before/after均一致。UI/input79、AudioContent78、Visual71。

两次成功实测均正常退出后冻结完整日志：

| 会话 | 连续记录 | 已证明 |
|---|---:|---|
| `20261021T101115225Z-0727ccd94dfa47aca9fceaa1a0ab23d8` | 930，0Error | MainMenu选择song0/generation1、收藏song0、改选song1/generation2；进入LevelList释放generation2；返回实际FakeMainMenu后创建sceneGeneration4新窗口、恢复song1/generation3；新按钮收到明确OperationUnsupported |
| `20261021T101619095Z-25269e51a2f74676a38b78e62a8dcf43` | 244，0Error | 新进程读取lastSong=song1、favorites=1，自动恢复song1；可见song0收藏标记。实际又出现窗口先于公共通知创建的次序，旧窗口和selection被释放后成功重建，无ID冲突 |

`runtime-proof.json`由`analyze-success.py`严格检查两份冻结日志的连续序号、无Error/Fatal、无WINDOW REFUSED/RELEASE FAILED/OBSERVATION FAILED、返回场景/新窗口/选择/按钮顺序及跨重启偏好。重启前后的偏好文件字节一致：lastSong=validation-song-1，favorites仅validation-song-0。游戏原生显示和点击由本轮Computer Use观察，未保存或解码截图payload。

中途一次启动经Steam重启转到“玩Rizline/启动Rizline Editor”选择框，前置进程仅记录2条bridge日志后退出，未形成可用游戏会话。`restart-early-exit-unverified-20261021T101541Z/`原样保留；选择已选中的“玩Rizline”继续后取得上述第二个成功会话。不能把2条日志或暂时没有窗口称作崩溃或宿主成功。

此前788包的325条初始化冲突失败保留在 `initial-scene-window-conflict-red-20261021T100535Z/`，不以新结果覆盖。789源码包含真实返回场景白名单和探针迟到通知显式Dispose两项修复。

结束后游戏已退出，`probe-files-restoration.json`记录6份临时文件恢复原先Absent，并已复核全部不存在。包括LOCAL config、metadata marker、偏好及3原创验证资产。此处恢复只适用于本阶段，其他阶段有各自记录。

范围仍有限：原生LOCAL音乐/封面播放、预览及重试/结果资源链未实现，Play明确拒绝；只有一次返回与一次进程重启，不代替B14完整双包焦点、硬件手柄、缩放/热拔、20轮压力等矩阵。B01–B19未全部验收。
