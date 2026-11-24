# 时区笔误更正

本包首次发出的hash-manifest SHA256为41ab337f9eee28c60a3e826aae0656d648828b7b78c01dbc1161c324b4d0b350。原timeline将host补测UTC 2026-11-02 23:41:31误换算为本地08:41:31；正确Asia/Shanghai为2026-11-03 07:41:31。此处只更正timeline与其生成脚本，测试UTC原始证据、退出码、检查数与源身份均不变。原timeline、生成脚本与旧manifest保存在history/timezone-correction；最终以重新生成的hash-manifest为准。
