# Camera scalar 本轮范围

- auth：用户授权 N3 局内相机真实标量角色开发与受控验证；不安装、不启动、不操作真实游戏或 GUI。
- in_scope：固定构建的少量完整方法/RUNTIME_FUNCTION 静态读取，独立 Camera SDK 合同、service/native 实现、受控 native-export tests；全部位于新的 D 盘原型目录。
- network_profile：离线 PE/dump 与当前进程自有 unmanaged 缓冲区、metadata/export delegates；未加载真实 GameAssembly/Unity。
- source_identity：GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；dump SHA256 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb`。
- 集成责任：root 接 Module、capability metadata、注册、run effect 审计和完整仓库测试；本原型由 root 收到 owned overlay 清单后统一集成。
- 单一属性：当前真实相机 rig 的 `Transform.localPosition.x`。其他 scale、postprocess 或未知 Volume/Bloom 不在本轮范围。
- 验收边界：33 项受控检查与 net6/net8 隔离编译；真实回调次序、持续可见效果、输入投影影响与停止时恢复留待实机验收。Interval=0 不能替代渲染次序证据。
