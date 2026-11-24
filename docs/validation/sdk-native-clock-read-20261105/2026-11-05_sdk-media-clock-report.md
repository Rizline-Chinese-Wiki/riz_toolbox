# SDK原生媒体时间读取候选

2026-11-05T09:28:59.887550+08:00 **下一离线工作继续收重启恢复R6／R11原句柄终态，并收SDK媒体时间读取的标准四库构建。SDK候选source-r2已完成17场景／127纯托管检查，owned-native纵向用例在独立目录准备。主仍X952，未安装／启动游戏。**

新增 `GameplayClockSnapshot.Playback` 可选typed读数，包含可用性、媒体秒、DspTimeService秒、rate、phase及Clock revision。候选只从已注册concrete provider／Session、实际七根source、receipt、唯一Clock及当前publication读取，不调用callback-only GetTime入口授权。读取不推进callback DSP watermark，也不由读取代码发布或撤回policy。Scheduled开始前沿用原未缩放delay。原native descriptor只供继任检测，暂缺时native项不可用，真实新session／epoch使旧控制代际过期，本次SetSpeed仍要求fresh exact descriptor。

最终源 `Z/sdk-native-clock-read-source-r1/source-r2` 共8路径，5改3加。origin-r2 SHA e98452c1e769447c1d2f7af622c8914dc1e8dcf29668ac7b897117fc390b73f5，patch SHA f9dcd53f642675d444a8b9493d9b8d57c79d767de908a3a876129dcc3059320e。独立审查发现并修复末尾native Verify后切歌可能留下旧game读数的窗口，r1原件保留。r2顺序为native最终验证→最后SameCapture→纯本地issuer／session／Clock／exactstate校验，最后一步无native getter／Resolve。

`validation-r2/managed-checks-result.json` SHA 532e21d7384648ec87b8017a3753d1b9c4c1d5c49847e54d9bdb62f437ba9134。实际17场景／127检查、编译与run均有原Popen句柄创建／退出FILETIME及OS0。涵盖paused／playing／scheduled与负delay、DSP无副作用、publication两fence竞变、两个plugin正式Service视图、final monotonic换revision、最终Verify换controller／run／roots、最后SameCapture换revision、native暂失及同地址新epoch。此批为纯托管逻辑和SDK／Adapter直接编译范围，不能签owned-native或新四库标准图。

首次validation-r1在compiler前遇generated行relativePath=null，保留自然exit1。直接Sdk／Adapter编译均OS0，各保留一条CS2023驱动警告。首次managed harness编译把跨框架CS1701升error，未执行case，失败保留；新compile-managed-checks-r2保留三条明确CS1701，其他警告仍视error，实际127通过。新 `Z/sdk-native-clock-standard-integration-r1` 将对source-r2组合的新四库做标准net6／完整PE-PDB与编译输入验证，结果另验，不复用旧包代签新源。`Z/sdk-native-clock-owned-validation-r1` 只准备新sdk-media-clock纵向模式，需新正式四库0和root复核后才能执行。

重启恢复R6 supervisor3892→driver42764，当前running-lower-time-wrapper。原resume42＋direct96只引用不重跑，余13后续门仍接edge8／fault2。R11 launch2220→supervisor16572→driver25712，当前running-engine-pitch-transfer-pending，仅补两未验收。原四成功273保持。旧两场景同秒1073807364的终止主体仍未知。新E012 supervisor8464→waiter29044已持三真实原句柄，配置SHA554e60b80d76741331ba3ea08deb6979e8bcdde6d41b2b37cf6638314a8f08d0，state=waiting-recovery-launch。三原OS0＋严格六项后才一次封存和strict/hash review；App47＋fresh另建新门，不能复活旧等待任务或把952旧主条件带入966新主验收。

新输出／cache／TEMP／备份保持D，fresh预留792915741B＋metadata余量，旧构建已清19.3GB。主952／history364／README本次逐SHA复核。真实Speed／Seek准入仍关，任意Seek、generation双ABA、其他writer／effects、生产DSP及B01–B19仍未整体完成。


2026-11-05T09:37:26.037135+08:00 **下一离线工作为SDK媒体时间的owned-native纵向验证准备／执行门，以及继续收R6／R11恢复实际终态。新SDK四库标准net6已经一次构建与完整508PDB校验通过，W0/E0。主仍X952，候选未应用。**

标准新图 `Z/sdk-native-clock-standard-integration-r1/build/build-complete.json` SHA 62b5a0092a671af5099d40b32db15243aa2c90f8da351548249a3a5db333953c。Sdk／Runtime／Core／Adapter四库均从新图fresh构建，506输入、496产品C#加12生成源，共508PDB文档。真实Csc ReferencePathWithRefAssemblies、产品ref/refint归属、analyzer／imports与实际／冻结源逐SHA通过。8 PE/PDB和8 reference assemblies均有固定SHA；实际compiler、verifier和外层driver各原Popen句柄自然OS0、完整创建／退出FILETIME。该标准图无CS2023或CS1701，先前direct-csc／net8 harness警告仍按原验证范围保留。新标准构建不代签原生功能。

Root另独立重核1016实际／冻结源路径、四项目全部编译引用和508文档归属、所有PE/PDB／copy-local及编译器日志，见 `Z/root-sdk-standard-clock-review-r1/test-result.json`。已通过17场景／127纯托管逻辑验证保持。`Z/sdk-native-clock-owned-validation-r1` 正准备单一sdk-media-clock纵向场景，必须引用本次四库并验其测试程序集PDB；root审查后才能实际执行。场景拟覆盖真实双plugin读paused／scheduled，actualReplay前后与GetTime对照、direct lower-time撤权及真实新Start/source-open/Pause后恢复同stablehost，最终roots0。尚未发生的行为不能签成功。

后续S3＋SDK真实产品源码合入差异已独立准备 `Z/s3-sdk-future-product-delta-r1/delta-manifest.json` SHA eedfdd5f110b7d4f293ffc02b511bf4629890ddd9443223858ae2d86d6344ffc。对R11-after-App47基线筛501产品路径，467同字节，9新增＋25修改全部为产品C#，没有删除，payload与preimage均已SHA核对。此包source-only、readyForApplication=false，无应用执行器。必须先有R11恢复E012／App47／fresh主域实际通过，再验S3/R6／edge8／fault2／SDK-owned及其准确新源码，另备份34路径并从应用后的真实新主构建；34路径不能混入原47路径门。

恢复R6当前running-lower-time-wrapper，新验收0项；旧resume42／direct96保持138。R11当前running-engine-pitch-transfer-pending，新验收0项，旧四成功273保持。新E0128464→waiter29044持三原恢复句柄等待真实全0。`Z/reboot-recovery-r11-to-main-preparation-r1` 只准备App47包／fresh静态合同和新E012观察门，未应用C、未验收fresh。旧1073807364终止主体仍未知，旧原件不覆盖。

main952／history364／README本次逐SHA再次保持，新输出／cache／TEMP／备份全部D，fresh保留792915741B＋metadata余量。真实Speed／Seek准入仍关，任意Seek、generation双ABA、其他writer／effects、生产DSP与B01–B19仍未整体完成。游戏／UI／账号／网络不操作。


2026-11-05T09:44:23.123323+08:00 **下一离线工作优先修SDK Read末尾Dispose重入。source-r2四库标准W0/E0和owned R1 harness编译已过，但这版禁止执行native新case，新增source-r3修订与实际反例验证在推进。R6/R11恢复原链继续运行。**

独立源码审查对照既有ValueTransactions.ClockCaptureOwnership合同，确认Service.Dispose／Host.Dispose在最后Guard之后的native Verify等getter里重入时，资源虽因_executing延后释放，当前Read却可能返回Success。既有合同要求本次Clocks.Read返回ScopeExpired，不能把这种读返回等同已提交操作的成功回执。基线也有类似末尾getter窗口，r2增加了一个新native getter重入点；不能把全部历史问题归因于本次新增。

新scope `Z/sdk-native-clock-read-source-r3` 将保留r2原件，只在最后可重入调用后、DTO发布前补Guard，并补Service／Host卸载的before／after真实纯托管反例。修订后须使用其新源码、新四库和新harness绑定，不能用r2构建验收r3。旧 `sdk-native-clock-standard-integration-r1` 构建范围仍有效，但不签此行为已修。owned R1 `build/build-complete.json` SHA0260d8a9c2a0a448fcae3a92d934d7427ee0ab5705644c5f294bd9aa2fa12364已自然0，61PDB／167refs／四条旧nullable warning；native case执行0。

标准17场景／127纯托管检查保持原范围，其中未覆盖这次Dispose尾窗。新功能native启动暂缓，原R6／R11恢复进程与新E012观察门保持各自原输入运行。source-only34路径未来delta暂仍绑定r2，后续需另source-r3新delta；当前未合主，README保护和D盘输出／fresh预算保持。任意Seek、generation双ABA、其他writer／effects、生产DSP与B01–B19仍未整体完成，真实游戏／UI／账号／网络不操作。


2026-11-05T09:59:23.693737+08:00 **SDK读取末尾Dispose窗口已实际复现并修复，source-r3新增9场景／71检查通过。下一离线工作为r3新四库标准构建和新owned harness绑定；R6／R11恢复原进程继续收终态。**

`Z/sdk-native-clock-read-source-r3/validation/managed-tail-result.json` SHA8d8b71dc01f40f1d2ced362269601417912be6bc72bcbbd6b4682c55a874199a。相同测试DLL先绑定旧r2正式产品，6场景／48检查实际确认服务销毁或插件卸载后本次Read错误返回Success；before明确featureAccepted=false。随后新Adapter净263源编译、完整265PDB文档核验及同DLL after9／71自然OS0。修复六例全部返回ScopeExpired且无Playback值，另验取消、正常双plugin和已dispatch回执保留。Service.Dispose在finally排空，Host.Dispose只停plugin scope，随后显式service cleanup排空，未混淆资源所有权。

r3相对r2只有GameplayService最终DTO发布前新增注释＋最后Guard两行，其他七overlay完整字节相同。source-r3-final SHAbe00db9d7cde7b0e973cea3ce1e67a888c264a575eee2bd6f7c4e2a71dd8736d，minimal patch SHA46e4b0701e501a32daad6e7a98d23453dcc9cf6152445834c6fe1c45f280aeeb。编译器、before、after、PDB verifier与外层均持原Popen句柄自然OS0和完整创建／退出FILETIME。此批未重跑原17／127，也不签r3标准四库或owned-native。net8 harness保留三项跨框架CS1701，新Adapter直接编译无源码警告。独立Root源／原件与进程证据核对见Z/root-sdk-read-tail-r3-review-r1/record.json。

新标准r3四库在 `Z/sdk-native-clock-standard-integration-r2` 组合，需新gate通过后一次fresh build与508PDB；后续 `Z/sdk-native-clock-owned-validation-r2` 需绑定其新DLL／PDB／refs再编译，native case仍待root复核。旧standard R1与owned R1编译通过按各自旧r2源验收，不能签r3；旧owned实际native执行0。34路径未来产品delta也仍source-only，待r3更新再另准备。

R6恢复仅补13场景后接edge8/fault2，R11恢复仅补2场景；旧6成功411检查保持引用，新链不重复。新E012观察与App47／fresh执行适配仍按实际新门，当前main952/history364/README已重新SHA核对且未合主。fresh保留792915741B＋metadata余量，新输出/cache/TEMP/备份D。真实Speed/Seek准入仍关，任意Seek、generation双ABA、其他writer/effects、生产DSP及B01–B19仍未整体完成，游戏/UI/账号/网络不操作。


2026-11-05T10:08:16.752104+08:00 **下一离线工作为SDK媒体时间 owned R2 的新测试程序集及实际纵向验证。包含Dispose尾Guard修复的source-r3四库标准图已一次W0/E0通过，508PDB完整校验。R6恢复wrapper新增自然0／96，旧2＋新1共234检查，当前继续raw-aba。**

新图 `Z/sdk-native-clock-standard-integration-r2/build/build-complete.json` SHA a048874ad928ee723885266c42cf214a1ccbf20e014f05ad4ec039399e335e86，preparation SHA f6199519c86c058129a3c20cca3db08093210fb5cd92825389877d855df1e820。Sdk／Runtime／Core／Adapter四库全部fresh标准net6，496产品C#＋12生成源，共508PDB。各库真实ReferencePathWithRefAssemblies为159／160／160／162，ref/refint归属与8 PE/PDB逐SHA通过。Compiler40672、PDB verifier17296及外层driver／WMI38200均原句柄自然OS0。冻结产物清单SHA e90996218d966d691a5e90476fe0cde480e2171ddee47150c8d5080d18921cf3。Root独立再次核1016实际／冻结路径和508符号文档，证据 `Z/root-sdk-standard-clock-review-r2/record.json`。

新图绑定source-r3原件与Dispose修复新gate，不能用旧standard R1代签。R3之前6／48真实复现旧错误、之后9／71修复通过，原17／127按原域保持。旧net8逻辑harness三CS1701、旧owned R1四nullable测试warning继续保留，各正式四库标准build本次W0/E0。新 `Z/sdk-native-clock-owned-validation-r2` 只迁移同一58源case到本次四库，目标61PDB／167refs，新的harnessbuild／rootreview后才一次sdk-media-clock原生场景；旧owned R1实际native执行0。

后续产品合入source-only包更新为 `Z/s3-sdk-future-product-delta-r2/delta-manifest.json` SHA b99a4fb342c2e3225474cbf362eb3a0bd3269b1d247d5b908ee34873bceb98d3，仍9新增＋25修改／0删除，相对R11-after-App47基线，包含最新source-r3。payload／preimage逐SHA通过。readyForApplication=false，无执行器，不混入原App47。后续须R6完整十五＋edge8／fault2／SDK-owned实测与各新源码门通过，再另准备34精确路径及应用后fresh主构建。

R6实际新增已验1项／96检查，当前running-lower-time-raw-aba；先前resume42＋direct96不重复。新wrapper真实23Prepare/23Commit、Scheduling撤权、原前原后各一次、旧Clock永久撤回、journal不改且roots0。R11恢复当前running-engine-pitch-transfer-pending，新增验收0项，旧四273保持。新E012与后继观察门仍按精确原句柄等待。App47/fresh执行适配 `Z/reboot-recovery-r11-main-execution-preparation-r1` 已prepare通过、实际修改仍0，root审查与新E012全部自然0后才能触发。已确认sourcePins／oldFacts／retention与47待改C路径交集0，应用后fresh按966核验，不能再套952旧主条件。

主952／history364／README本次复核保持，游戏／UI／账号／网络不操作。新输出／cache／TEMP／备份在D，fresh792915741B＋metadata余量保持。真实Speed/Seek准入、任意Seek、generation双ABA、其他writer/effects、生产DSP与B01–B19仍未整体完成。


2026-11-05T10:16:06.820629+08:00 **下一离线工作收SDK owned R2实际用例终态与原R6/R11恢复。SDK新case已审核并一次启动，App47→fresh新耐久controller也已一次启动等待新E012全0门，尚未合主。**

SDK门 `Z/root-sdk-owned-case-review-r2/review.json` SHAd0ddf215e8ce386b7178ca23e846d3109ca2e971e1f3814d42cd35f067819aeb，独立review SHAe7b1856a220f4aa9711ca70c4f0c9de7833d70a09f01f58175ede5abb7f1be4a，Root另核122实际/冻结源路径、61PDB、167refs、8新copy-local产品及native五输入。owned R2 build SHA7ce383f3ad68238b824da170e3a26e729b28daa13fc37f6f2435517e4aaeaa12，preparation d01be2b538cccfdc4be8410061661b2461f5b0a75c141d9841f75387b7f4eba5，四库来自source-r3标准R2 W0/E0。测试仍保留四条既有CS8602。唯一WMI case supervisor37924→driver21452→实际case37144，原创建FILETIME见各execution.json；native逻辑尚未终态，不能标成功。入口 `Z/sdk-native-clock-owned-validation-r2/build/case-sdk-media-clock`，最终case-result.json及supervision-case-result.json分别验。

App47/fresh完整适配经Root及独立审查，rootreview `Z/root-recovered-main-execution-review-r1/review.json` SHAfbedd6fef83537eb4db4c89a95577009357f87dca10f7a247ecd4feefedfbb71。固定contract SHA23446a5f0172e15f585df7a0af8cab0806ac4a6f0ab69e805d4bf6e5da760f39，唯一WMI controller33168→driver34832，state=waiting-new-observer-launcher，实际持原observer16336／24380精确句柄。门要求新恢复E012全层自然OS0、六场景严格摘要、sealed retention／strict/hash review，之后才47路径原子应用，再从实际main966 fresh编译三变体／short52／11PE-PDB与40字节对比。两条readonly prepare已真实OS0，所有重复pins与47改动C路径交集0，fresh禁止952旧基线loader。当前App/Fresh未执行，34S3＋SDK产品delta不在47内。

新增独立耐久原句柄观察器 `Z/durable-active-outer-observer-r1`，WMI supervisor36844已启动，内waiter同时持有SDK supervisor37924与主controller33168两个精确创建FILETIME句柄，state=waiting-sdk-owned-case-supervisor。逐个真实退出后写独立process-result，外层再持其Popen自然wait。此观察仅采证不启动测试／应用；可在会话切换后继续收取终态。已有工具observer session73538可继续自然等，不能终止运行测试或重复启动。

R6旧2＋新wrapper共3场景234检查已过，raw-aba原进程续跑，随后其余core／typed6／edge8／fault2按原恢复门。R11仍在transfer-pending恢复，旧4共273保持。主952／history364／README本次SHA复核，真实游戏／UI／账号／网络不操作；D输出／cache／TEMP／备份与fresh792915741B＋metadata余量保持。任意Seek、generation双ABA、其他writer/effects、生产DSP、整体S3与B01–B19尚未完成。
