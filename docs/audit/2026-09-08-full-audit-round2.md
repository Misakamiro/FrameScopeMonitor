# FrameScope 全项目复审审计 + 实机全量测试报告(Round 2,2026-09-08)

> 方法:三路并行代码深挖(Rust 宿主与服务 / C# 采集链与构建链 / 前端与数据一致性)+ 关键发现人工逐条核实(标注 ✅核实)+ **实机全量测试**(UI 走查、硬件采集、图表链路、性能占用、CS2 与战地 6 真实游戏全链路)。
> 基线:HEAD = a9f36e4(分支 codex/nexbox-frontend-preview)。上次审计:2026-09-08-full-audit.md(高危 8 / 中危 ~20 / 低危 ~25),其后修复提交 28fb00d、3fcea46、8611ecc、5fb6492、a9f36e4。

---

## 〇、结论摘要(TL;DR)

1. **5fb6492(H4 修复)引入了高危回归:硬件服务 100% 无法启动**。`FILE_FLAG_FIRST_PIPE_INSTANCE` 常量写成 `0x0005_0000`(真值 `0x00080000`),且该标志被加在 accept 循环每一次迭代上。实际后果:管道创建必然失败(错误 87)→ 服务进程秒退 → 前端永久"硬件信息加载中"。**交接文档交付时的部署产物即处于此损坏状态**(部署目录的服务 exe 为 01:04 旧版,恰好还没包含这个坏修复;全量重建后才暴露)。**已在本次测试中修复并实机验证**(见 §二 R-A1/R-A3),修复仅在工作树,待提交。
2. **H8(高危)未修复**,与 HANDOFF"全部高危已修"的声明不符(见 §三)。
3. 实机测试抓到一个静态审计漏掉的新高危:**桌面模式本地模拟状态机与真实对账并行运行,可向真实报告列表插入模拟报告**(R-C12)。
4. **真实游戏测试**:CS2(VAC)全链路完美(25466 帧、119.3 FPS 完整报告);战地 6(EA Javelin 内核反作弊)**硬件采集与 OSD 正常,但 PresentMon 的 ETW trace 会话被反作弊拒绝**,软件正确降级为诊断报告且归因清晰(`presentmon-etw-access-denied`)——这是反作弊环境下的预期行为边界,非软件缺陷。
5. 性能达标:监控全链(C# watcher/worker + 双 sampler + Rust 服务 + LHM provider + Tauri 宿主)合计 CPU ≈1.1%(16 逻辑核)、内存 ≈340MB。
6. 上次审计 8 项高危中 6 项修复验证通过(1 项即上述回归、1 项未修);中危大部分已修或降档,遗留 9 项不变;新增发现 高 3 / 中 8 / 低 ~20。

---

## 一、修复质量验证(上次审计 H1-H8 / M1-M18)

### 高危

| 项 | 结论 | 证据与说明 |
|---|---|---|
| H1 报告按钮不可达 | ✅修复正确 | ReportsPage/TargetsPage 改 `backendInvoke`;实机验证:报告详情弹窗、"打开图表报告"成功拉起浏览器 HTML 报告 |
| H2 进程列表假数据 | ✅修复正确,有留尾 | 桌面端初始空列表;但 `TargetsPage.tsx:25` 声明的 `processesUnavailable` 从未在 JSX 渲染——失败时下拉空白,无错误提示(提交声明未完全兑现) |
| H3 列名不匹配 | ✅修复正确,有留尾 | 修法为改记录端表头(与审计建议相反方向)。逐列核对:16 列表头与值序对齐,读端 `ReadSystem` 与 system-samples 共用读取器自洽;实机验证新表头(GPU PowerW/MiB 列)真实落盘。**留尾**:①旧 CSV 兼容性=静默降级(旧 run 的 4 列 overlay 覆盖不生效、无诊断提示,报告不缺图不报错);②审计建议的 `overlayMergedRows` 写入元数据未做(`FrameScopeReportGenerator.cs:63` 死变量);③source-boundaries 列名测试锁两侧字符串,拦不住读写列序漂移(弱断言) |
| H4 管道缺 FIRST_PIPE_INSTANCE | ⚠️**修复引入高危回归(R-A1/R-A3)** | 见 §二。本次已修复并实机验证 |
| H5 缓存无 TTL | ✅修复正确 | overlay_cache 5s TTL、ping 5 连败退役、FPS 过期清 lows 逐一验证正确。留尾见 R-A6 |
| H6 preview_native 阻塞主线程 | ✅修复正确,有留尾 | async fn + spawn_job 全分支核对无主线程阻塞。留尾:`recv()` 无超时(卡死时泄漏 tokio worker 线程)、`hardware_service::call` 无超时读发生在 async worker 上、backend.rs `output()` 无界等待与注释不符 |
| H7 CLIENT 锁贯穿 I/O | ⚠️部分修复(核心未修) | spawn+重试已移出锁 ✓;但**快路径仍持 CLIENT 锁做无超时 ReadFile**(`hardware_service.rs:211-223`),服务挂起时 1 个请求即可永久占锁,poller 与全部 UI 硬件命令串行冻结。ReadFile 超时(CancelIoEx/overlapped)未做 |
| H8 targets 同步闩锁+整表回写 | ❌**未修复(维持高危,✅核实)** | `state.ts:103/204-208` 一次性闩锁仍在(先置位后同步,失败不重试);`state.ts:192-195` 整表回写仍在。触发链核实:后端未部署/目录不可见时 `targets_get` 失败 → 仅 console.error → 用户此后任何目标编辑把 localStorage 陈旧整表(或首次运行的硬编码 cs2/valorant 两项)原子写回覆盖真实 targets;空列表保护只防读方向,反向会让已删目标复活。**与 HANDOFF 第五节"全部高危已修"声明冲突** |

### 中危

| 项 | 结论 |
|---|---|
| M1 suspended 无法解除 | ✅修复正确(force_reset 清 suspended+失败计数;受 R-A2 无界 wait 影响) |
| M2 >1MB 截断失步 | **降档为低**。MESSAGE 模式下 ReadFile 失败即回收连接重连,"永久失步"不可达;现实 payload <100KB 几乎不触发。建议仍补 ERROR_MORE_DATA 循环 |
| M3 孤儿服务/stop 不可观察 | **未修(中)**。无 Job Object;全仓无客户端发 stop。R-A1 修复后此风险回归显性 |
| M4 session 线程无上限 | **未修(中)**。JoinHandle 无限累积,与 M3 合并修 |
| M5 三角洲密码地图空操作 | **未修且加重(建议上调高)**。三层全废:①`core.ts:73` 强制清空 maps;②`OverlayPanelPage.tsx:659` 调用的 `get_delta_passwords` 命令在三张命令表都不存在→地图列表永久"加载中";③core.ts 20 项白名单 vs Rust 默认 22 项→首次保存设置即静默丢"游戏延迟"和"三角洲密码"行(见 R-C2) |
| M6 config.update 不落盘 | **未修(中)**。Rust 无 config_save 命令;设置页"已保存"但采样间隔/日志/目录全部不生效;pollIntervalMs 连 C# 侧都被强制 1000,前端 250-10000 范围是双重谎言 |
| M7 config 双宿主 RMW/抹字段 | **降档为低**。Rust targets_save 用 Value RMW 保留未知字段(实机验证 VisualEffectStrength 保留);字段抹除仅在 C# Save 时发生,当前触发窗口极小 |
| M8 临时文件名践踏 | ✅修复正确(AtomicU64 序号+原子 rename;留尾:rename 失败不清理临时文件) |
| M9 O(N×M) 归并 | **未修,量化修正(中)**。2h@1Hz ≈0.5-2s(原"数十秒"偏悲观);真正的风险在超长采集:24h ≈40-90s,逼近 120s 生成超时 → 报告 timedOut |
| M10 Join 预算 | ✅修复正确,有留尾(a9f36e4 Join(8000) 覆盖最坏 ~6s 交换;未做迭代 deadline——服务不可达时实际采样率降到 ~1/3Hz,行缺失非错值;Join 超时后仍可能落一行超界数据,报告容错覆盖) |
| M11 idle→completed 误报 | ✅修复正确(前端 completedRuns 基线 + 后端输出 CompletedRuns;但基线只在 waiting 捕获→引入 R-C1,见 §二) |
| M12 alreadyRunning 采 A 显 B | ✅修复正确(实机两次验证:捕获 CMD Test 和 BF6 时 UI 目标下拉自动对齐) |
| M13 粘性合并 | ✅修复正确(整体替换+requestId;留尾:HardwarePage 轮询无乱序保护(低);宿主 poller Err 不 emit → 悬浮窗冻结最后快照(R-C5,低)) |
| M14 传感器任意第一兜底 | ✅修复正确,有引入(R-A4:严格匹配下 ssd_temp 第一调用传空提示=死代码,常见 NVMe 盘温度恒 null;R-A5:裸 "GPU" 提示仍可误配显存温度,条件性) |
| M15 provider kill 绕过优雅退出 | ⚠️**修复引入高危问题(R-A2,✅核实)**:`provider.rs:212` 优雅路径 `Child::wait()` 无任何超时,provider 挂起(正是 kill 的目标场景)时永久阻塞且持有 provider Mutex → 服务全冻结;提交信息宣称的 3s 等待不存在。旧代码反而安全 |
| M16 部署强拷 | ✅修复正确(部署前停 7 个进程;留尾:不等待进程真正退出,慢退出仍可能 IOException) |
| M17 build.ps1 脏状态/版本漂移 | **未修(中),本次实机复证**:部署目录 HardwareService=01:04、C# 链=01:44、宿主=08:10,落后 HEAD 两个修复提交——版本漂移再次发生;且 tauri build 因 exe 被占用静默失败一次(npm build 成功掩盖了链接失败,exit 0) |
| M18 长小数/key 重复 | 部分:服务端圆整使长小数实质化解(降低);TargetsPage `key={process}` 仍在(低) |

---

## 二、新发现(本轮)

### 高危

**R-A1 ✅已修复(本次)| `src/hardware-service/src/main.rs:84`**
`FILE_FLAG_FIRST_PIPE_INSTANCE` 常量 `0x0005_0000` 错误,真值 `0x00080000`(本机 winbase.h 实证;0x00050000 含合法 WRITE_DAC + 未定义位 0x10000,MSDN:含列表外标志必失败)。CreateNamedPipeW 返回错误 87 → 非特判的 ACCESS_DENIED → `exit(2)`:**5fb6492 之后构建的服务一次都起不来**。实机复现:服务启动 2 秒内退出,日志 `named pipe error: 参数错误 (os error 87)`;前端永久"硬件信息加载中";宿主吞掉 spawn 失败(低危项)使故障无声。
**修复(工作树,待提交)**:常量改正为 `0x0008_0000`;同时仅首次创建断言 FIRST_PIPE_INSTANCE(见 R-A3);ACCESS_DENIED 静默退出前排空活跃 session。实机复验:服务存活、双实例防重正确、客户端多连接正常、CS2/BF6 全链路跑通。

**R-A2 ✅核实 | `src/hardware-service/src/provider.rs:206-218`**
kill() 优雅退出 `Child::wait()` 无超时(提交信息宣称的 3s 不存在)。provider 挂起(LHM 内核驱动卡死,正是 kill 存在的场景)→ wait 永久阻塞 + 持有 provider Mutex → 服务 dispatch 冻结;叠加宿主侧无超时 ReadFile(H7 留尾)→ 1Hz poller 与全部硬件命令永久冻结,仅重启可解。**修法**:优雅路径 try_wait 轮询 ≤3s,超时转 kill;并补客户端 ReadFile 超时。

**R-C12 ✅核实(实机抓获)| `desktop-preview/src/preview/runtime/state.ts:74-88`**
**桌面模式下,为浏览器设计的本地模拟状态机仍在每秒推进**(tick 的 mutatePreview 分支无条件运行)。桌面正常时每秒对账(syncDesktopMonitor)会把本地相位打回真实状态,但只要 `monitor_status` 对账失败一次(catch 静默),本地状态机即接管:waiting→capturing→34 秒后 finishReport() **向真实报告列表 unshift 一条模拟报告**(simulated:true,id=preview-*,FPS 为 fixture 值)。
实机捕获:战地 6 测试中 13:40:32 主页出现"Battlefield 6 · 34 秒 · 144.0 FPS"报告(时间戳/时长/FPS/目标名全部为模拟值),而后端报告列表无此报告;报告页刷新后被冲掉。ReportsPage 不渲染 simulated 角标(既有低危),用户无法分辨。
**修法**:tick 的模拟状态机包进 `if (!isDesktop())`;同时给 ReportsPage 补"模拟"角标。

### 中危

| # | 位置 | 问题 |
|---|---|---|
| R-A3(已随 R-A1 修复) | main.rs:139-157 | FIRST_PIPE_INSTANCE 加在 accept 循环每次迭代:即便常量修正,第一个客户端连入后的重建必得 ACCESS_DENIED → 被误判"冗余 spawn"静默退出(跳过 join)。MSDN 明示该标志只可用于首实例 |
| R-A4 | overlay.rs:190-191 | 严格匹配下 `ssd_temp` 第一调用传空提示列表恒 None(死代码);盘名不含 "Drive"/"SSD" 的常见 NVMe(WD SN850X、KIOXIA 等)硬盘温度永久 null。测试样例恰含 "SSD" 未暴露 |
| R-A5 | overlay.rs:163,166 | 裸 "GPU" 提示可误配显存温度/SOC 功耗(行序首个命中),M14 仅部分消除 |
| R-A6 | game_fps.rs:940-950,990-992 | FPS 过期清 lows 被 `!=0` 门控,切非游戏路径先置 0 不清 lows → 1%/0.1% Low 滞留上局数值并被 OSD 持续输出 |
| R-A7 | pawnio.rs:109-121 + lib.rs poller | PawnIO 安装前 taskkill 杀服务,但 1Hz poller 5s 冷却后重拉 → UAC 等待期间 LHM 轮询必然恢复,防护被击穿;taskkill /IM 全系统按映像名杀 |
| R-B1 | Client.cs:84-111 + ProcessCleanup.cs:79-124 | C# spawn 的硬件服务永久残留(全 C# 链无 stop、清理名单不含服务/provider);老服务"挂起但存活"时新 spawn 一律 ACCESS_DENIED 退出 → 挂起实例永久占据管道名,仅外部杀进程可解 |
| R-B2 | SystemSampler.Gpu.cs:27-28 | nvidia-smi 读取 `ReadLine()` 在 WaitForExit(2500) 之前同步阻塞,驱动挂起时 system-samples 整体停更 |
| R-C1 | state.ts:151-162 | completedRuns 基线只在 waiting 相位捕获:**游戏已在运行时点"启动监控"(主场景)跳过 waiting → base=当前值 → 成功采集恒判 error**("采集进程异常退出"),报告实际已生成。CS2 测试因先 waiting 后捕获未触发;建议按中优先修 |
| R-C2 | core.ts:72-73 vs overlay_panel.rs:53-76 | 首次悬浮框设置保存即以 20 项白名单整体覆盖 Rust 22 项默认,静默丢"游戏延迟(默认开)"与"三角洲密码"行 |
| R-C3(并入 M5) | OverlayPanelPage.tsx:659 | `get_delta_passwords` 三表均无 → 密码地图弹窗永久"加载中" |

### 低危(摘要;详见三路审计原始结论)

Rust:log 未初始化、format_unix_local 慢 8h、parse_run_timestamp DST、WSAStartup 不 Cleanup、WinEventHook 死代码、FPS_STARTED 闩锁、disk_status 语义、spawn 错误吞、1MB 缓冲、响应 id 不校验、provider stderr 覆盖、set_sampling_interval 全局影响、strip_exe_suffix ETW 回调内 panic(abort)、GDI 拖动位置不落盘、WM_QUIT 外层空转、update_overlay_settings 绕过 OVERLAY_OPERATION 锁(R-A8)、export_hardware_report 任意路径写(R-A9)、find_report 重复整读 data.js(R-A10)。
前端:HardwarePage 轮询无乱序保护、localStorage 无界(浏览器模式)、`target-${Date.now()}` 撞 id、`?id=` 失配、config 双重校验不一致、加载失败永久"加载中"(**实机验证:服务修好后旧窗口仍卡加载中,重启应用才恢复**)、busy 三处重复、SensorMonitorPage fetchingRef 卸载窗口(R-C7)、LazyStore 无 catch(R-C9)、report.regenerate 非空断言(R-C10)、monitor.stop 按进程名全系统杀(R-C11)、OverlayPanelPage 维护从不渲染的 hardwareData(R-C6)、browser fixture VRAM 量纲差 1024 倍(R-C8)。
C#/构建:watcher 对秒退 worker 无冷却(R-B3)、Quote() 尾反斜杠(R-B4)、安装/卸载 kill 名单缺服务与 provider(R-B5)、ProcessSamplingMode high-precision 不可达(R-B6)、IsWatcherRunning 裸 PID(R-B7)、MonitorSession 异常路径不停 recorder(R-B8)、EnsureServiceRunning 无冷却、ReadLine 滞留线程池、status.json 无 hardware-samples 证据、8611ecc 提交信息与 diff 不符(M10 实际在 a9f36e4)(R-B9)。

---

## 三、文档与流程问题(与 HANDOFF 的出入)

1. HANDOFF 第五节称"全部高危与绝大多数中危已修"——**H8 完全未修**;5fb6492/a9f36e4 虽在 HEAD,但**部署产物未更新**(M17 漂移),交付的实机运行的是没有这两个修复的旧二进制。建议 HANDOFF 补记。
2. `docs/preview/IMPLEMENTATION.md` 缺审计与全部 5 个修复提交的实施记录条目。
3. HANDOFF 第六节称"测试目标 cmd.exe 已停用"——实测 **CMD Test(cmd.exe)处于启用状态**,且实机被 watcher 立即捕获开始采集(本次测试已改为禁用;同批将 Battlefield 6 改为启用)。
4. 8611ecc 提交信息声称的 M10 修复实际不在该提交中(R-B9)。

---

## 四、实机全量测试报告

环境:Ryzen 7 7800X3D / RTX 3080 10GB / 32GB DDR5 / Win11 专业工作站版 2560×1440@199Hz;管理员 shell;全量重建(build.ps1 + cargo + tauri build)并部署(产物时间 13:09-13:12)。

| # | 测试项 | 结果 |
|---|---|---|
| 1 | 应用启动与页面走查(主页/硬件/目标/报告/悬浮框) | ✅ 全部正常渲染,液态玻璃 UI、数据卡片、开关、下拉均工作 |
| 2 | 硬件采集数据正确性 | ✅ 服务直连 36ms 返回 38KB 真实快照;采集期 hardware-samples.csv 严格 1Hz 增长;数值与真实硬件吻合(7800X3D 4.95GHz/58°C、3080 36W/50°C/6.9GB) |
| 3 | 监控启停 | ✅ 启动(waiting"等待目标进程")→ watcher/worker/双 sampler/PresentMon 全链拉起;停止全部退出 |
| 4 | 目标自动发现(M12) | ✅ CS2 13:30:46 启动 → 1 秒内捕获;BF6 同样;UI 目标下拉自动对齐实采目标 |
| 5 | 图表链路(历史+新报告) | ✅ 报告详情弹窗、打开图表报告(浏览器 HTML)、全部图表渲染;新 CS2 报告 25466 帧/119.3 FPS/系统样本 370;报告自动打开(OpenReportOnComplete)生效 |
| 6 | CS2(VAC 反作弊)全链路 | ✅ 完整:发现→采集(3分33秒,9 个 CSV)→ 退出 → 完整报告自动生成(charts 三件套)+ 自动打开;FPS/1%Low/0.1%Low/GPU 温度功耗 VRAM 全有数据 |
| 7 | 战地 6(EA Javelin 反作弊) | ⚠️ 符合预期边界:自动发现 ✓、硬件采集全程 ✓(461 样本)、**OSD 在游戏画面上正常显示且实时(fps 125/GPU 57%/CPU 62%)**、FrameScope 自有 ETW FPS 会话工作 ✓;**PresentMon 的 ETW trace 被反作弊拒绝**(`failed to start trace session: access denied`,同样管理员权限下 CS2 成功)→ 游戏退出后正确降级为**诊断报告**,`FrameCaptureStatus=presentmon-etw-access-denied` 归因清晰 |
| 8 | 性能占用(采集满负荷) | ✅ 全链合计 CPU ≈1.1%(16 线程)、内存 ≈340MB(provider 87MB 为最大);对照 cs2 自身 4.6%/2.8GB |
| 9 | fail-closed 行为抽查 | ✅ 缺传感器显示 "--"(硬件页显示器卡分辨率/刷新率 "--");诊断报告明确"未采集到 FPS 数据"及原因 |
| 10 | targets_save 落盘 | ✅ UI 开关改动原子落盘;未知顶层字段(VisualEffectStrength)保留 |

测试中的环境变更(留档):CMD Test 目标禁用、Battlefield 6 目标启用(经 UI 操作,顺带验证 targets_save 链路);FrameScopeHardwareService.exe 的 FIRST_PIPE_INSTANCE 修复已重新构建并部署(部署目录 13:16 后为修复版)。

### 实机测试抓到、静态审计漏掉的问题
- R-A1 flag 值错误(静态审计误信常量正确;实机 100% 复现)。
- R-C12 模拟报告混入(需真实启动监控的时序才暴露)。
- 前端"加载失败永久加载中"(低危)的实际影响被放大:服务恢复后旧窗口不自我修复,必须重启应用。
- tauri build 在 exe 被占用时 vite 成功、链接失败,但整链 exit 0(M17 家族新增证据)。

---

## 四·补、核实为非缺陷的新条目(防下轮重复)

继承上次 12 条之外,本轮新增:WebBridge 报告命令注入面已封死(路径权威重建);BoundedProcessRunner taskkill 无注入;ReportOpen 浏览器链安全;Diagnostics Substring 截 JSON 只处理自家文件无危害;status.json 原子写(JSONFile temp+Replace);CSV 解析健壮(BOM/越界/畸形行容错);硬件 CSV 单写者无漂移路径;时间轴对齐自洽(±2s 最近邻 + AlignPresentMonTime);M2 "永久失步"不可达;monitor_status 撕裂读不成立;get_overlay_metrics 无调用方(性能问题暂无危害);poller decode_fixture 与服务字段逐一吻合不会误杀;COMMANDS 白名单与分支一一对应(43 项);shell_open cmd 注入不可达(Rust 单参数引号化+文件名禁 `"`);serve_session 对畸形输入健壮;write_json_atomic 并发安全;start_fps_monitor 非管理员路径干净;find_report 前 50 条上限为有意设计;FrameTimeBuffer/EMA/百分位算法正确;ETW 会话生命周期无句柄泄漏;竖排窗 GB 标签正确(服务端已换算);HardwarePage /1024 无双重换算;VerticalOverlayPage 北京时间为有意设计。

---

## 五、建议修复顺序

1. **立即(已做 1/3)**:R-A1/R-A3 已修复待提交;**R-A2**(provider wait 加 3s 上限)与 **R-C12**(tick 模拟状态机加 `!isDesktop()`)为两处小改,建议同批;**H8** 最小修法(同步成功才置闩 + 未同步禁止回写)两行守卫。
2. **本批**:R-C1(基线捕获补 capturing 分支)、R-B2(nvidia-smi 读超时)、R-A4(存储温度匹配)、R-A6(lows 清理移出门控)、M5/R-C2/R-C3(三角洲三件:并集保留未知项 + 实现或移除 get_delta_passwords 入口)。
3. **计划内**:M3/M4(Job Object + 线程上限 + stop 语义)、R-B1(C# 侧 stop/清理名单)、M6(config_save 或明确标注)、M9(双指针归并,≥12h 采集前必须)、M17(构建/部署原子化 + tauri build 失败可见性)、H7 留尾(ReadFile 超时)。
4. **加固排期**:上次低危清单 + 本轮低危(管道 ACL、backend_root 校验、可观测性:overlayMergedRows 元数据、status.json hardware-samples 证据、log 初始化)。

---

*审计与测试:ZCode,2026-09-08。三路深挖原始结论已并入本报告;关键条目均经人工行号级核实或实机复现。工作树含未提交修复(main.rs FIRST_PIPE_INSTANCE),提交与否由用户决定。*
