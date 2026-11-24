# 技术依据

CanvasControl.GetInformations RVA0x3e3620：strict speed scan0x3e3764、range converter0x3e3815、mul/add floor0x3e382c/0x3e3834；strict x scan0x3e38d4、Single差转Double epsilon0x3e399a-0x3e39b4、ease/clamp/lerp/width0x3e39b6-0x3e3a31。

固定指令f2440f101d07153e01在0x3e36b0计算RIP常量0x17c4bc0，标准.NET PEReader只读核验bytes8dedb5a0f7c6b03e为Double1e-6；clamp One0x17c4bf4为Single1。constants.log保留读出，GameExecuted/BinaryMapped均false，读取前比对固定DLL SHA。

CanvasControl.SetPosition RVA0x3e3aa0先floor*speed，再逐component乘downVector(0x24)，之后x乘rightVector(0x3c)并add。当前profile只列up/right/forward，后续actual source必须补downVector证据及值读回，不能假称已捕获。
