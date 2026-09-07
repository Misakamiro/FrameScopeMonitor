# FrameScope 全项目审计报告(2026-09-08)

> 方法:三路并行深度审计(Rust 采样服务与宿主 / 前端运行时与页面 / C# 接入面与构建链)+ 关键发现人工逐条核实。
> 范围:src/hardware-service、desktop-preview/src-tauri、desktop-preview/src、src/(采集链接入面)、build.ps1 与部署脚本。
> 已知设计约束(不计为缺陷):fail-closed 数据缺失返回 null;csc 4.0.30319 仅支持 C#5;FPS 双源为用户拍板保留。

## 统计

- 高危:8 条 | 中危:约 20 条 | 低危/加固:约 25 条
- 关键发现均经人工核实(标注 ✅核实)

---

## 一、高危(建议立即修复)

### H1 ✅核实 — 桌面端"打开图表报告/报告目录"按钮 100% 失败
- **位置**:`desktop-preview/src/preview/pages/ReportsPage.tsx:35,42`;`src/preview/runtime/core.ts:6-14,98`
- **问题**:页面用裸 `invoke("report_open"/"report_open_directory")`,但 core.ts 的 switch 与 `nativeCommands` 白名单均不含这两个命令 → 必抛 `Unsupported preview command`。Rust 宿主与 backend.rs 的实现完整却不可达;错误被 setOpenError 直接显示给用户。
- **触发**:桌面端对任何真实报告点这两个按钮。
- **修**:改用 `platform.ts` 的 `backendInvoke`;并为三张命令表(switch cases / nativeCommands / backendOnlyCommands)与 Rust COMMANDS 做一致性静态测试(挂载到现有 source-boundaries.test.ts)。

### H2 ✅核实 — 目标页进程列表永远加载不到,静默展示 4 个假进程
- **位置**:`TargetsPage.tsx:26,32`;同上命令路由问题
- **问题**:`invoke("processes_refresh")` 不可达,异常被 `catch{}` 吞掉 → 桌面端永远显示硬编码的 cs2/VALORANT/TslGame/GamePreview 4 个示例进程,以"预览进程列表"名义冒充真实窗口列表。后端含窗口标题的 250 条真实枚举成为死代码。
- **修**:改 `backendInvoke`;失败时 UI 提示"进程列表不可用"而非静默假数据。

### H3 ✅核实 — 硬件快照列名与报告合并不匹配,GPU 合并 7 列只有 3 列生效
- **位置**:`src/monitoring/FrameScopeHardwareServiceClient.cs:125-130`(表头 `GpuPowerW/GpuMemClockMHz/GpuVramUsedMiB/GpuVramTotalMiB`)vs `src/reporting/FrameScopeReportGenerator.SystemData.cs:34-41,77-83`(读取 `PowerW/MemClockMHz/VramUsedMiB/VramTotalMiB`)
- **问题**:报告按列名取数,四个名字对不上 → 永远 null 不覆盖。即数据一致性改造中 GPU **功耗/显存频率/显存占用/显存总量**四列的同源合并是死代码,只有占用/温度/核心频率三列真正生效,且无任何诊断提示。
- **修**:统一列名(推荐改报告读取侧),把 `overlayMergedRows` 计数写入报告元数据防再犯。

### H4 — 服务端管道缺 FIRST_PIPE_INSTANCE,双服务/双 LHM 并发成为可能(多客户端改造引入)
- **位置**:`src/hardware-service/src/main.rs:137-148`;`desktop-preview/src-tauri/src/hardware_service.rs:81-84`;`src/monitoring/FrameScopeHardwareServiceClient.cs:84-111`
- **问题**:`PIPE_UNLIMITED_INSTANCES` 且无 `FILE_FLAG_FIRST_PIPE_INSTANCE` → 第二个服务进程可成功创建同名管道,不触发注释假设的"名字冲突退出"。Tauri 与采集 worker 双方都有 spawn 兜底,probe 落入 accept 重建窗口即双服务——各持一条 LHM provider 并发读(已知传感器冲突来源)。且全仓无任何客户端发 `stop`,重复 spawn 的服务**永久残留**。
- **修**:加 `FILE_FLAG_FIRST_PIPE_INSTANCE`,第二实例 `ERROR_ACCESS_DENIED` 即退出;spawn 侧把该错误视为"已有实例";客户端退出发 stop 或服务加空闲超时。

### H5 — 悬浮框数据缓存无 TTL,过期值永久冒充当前值(fail-closed 破口)
- **位置**:`src/hardware-service/src/overlay.rs:236-271`;`lib.rs:245-269`;`game_fps.rs:937-948`;`overlay_panel.rs:1635-1637`
- **问题**:`overlay_data_response` 每次成功采样后 `merge_overlay` 把 null 字段用缓存旧值回填并写回缓存。game_fps 的"3 秒无帧 FPS 归零"被整体架空——归零的 fps 立即被旧值填回;ping 失败永不失效;provider 挂掉时整包回放缓存;宿主 `CURRENT_HARDWARE_DATA` 死后不清空。三层叠加,悬浮框可能长期显示一套过期数据。
- **修**:缓存带时间戳,carry-forward 仅 ≤3-5s 生效;ping 连续失败 N 周期置 null;宿主 poller 连续失败 K 次后写全 null 快照。

### H6 — `preview_native` 同步阻塞主线程,多个命令可致整窗冻结
- **位置**:`desktop-preview/src-tauri/src/lib.rs:104-116,184-194,197-216`
- **问题**:handler 阻塞主线程消息泵(注释自证)。`report_regenerate`(recv 无超时等生成器,backend.rs `output()` 无界等待)、`install_pawnio_driver`(等 UAC 交互任意长)、全部 `hardware_service::call`(继承 H7 的阻塞)都在主线程执行 → 生成器卡住/UAC 未处理/provider 慢时整窗冻结。
- **修**:三命令改 `#[tauri::command(async)]` 或 spawn_job;`report_regenerate` 用 `recv_timeout` + 超时杀生成器。

### H7 — 管道客户端 CLIENT 锁贯穿连接重试与无超时 I/O
- **位置**:`desktop-preview/src-tauri/src/hardware_service.rs:208-251,145-157`
- **问题**:`call()` 持锁完成 spawn+20×300ms 连接重试(最坏 ~6.3s)与无超时阻塞读写(服务端 provider 读超时 8s;服务挂起时无限期)。1Hz poller 与全部 UI 硬件命令串行排队,悬浮框停更。
- **修**:重试与 I/O 移出锁临界区(锁内只做检查-标记-连接);transport 失败先归还锁;ReadFile 加超时(CancelIoEx 或 overlapped)。

### H8 ✅核实 — targets 一次性同步失败不重试 + 整表回写,可覆盖销毁真实目标配置
- **位置**:`desktop-preview/src/preview/runtime/state.ts:103,160,167,170-186`
- **问题**:启动同步失败后 `targetsSynced` 永久置位,不再重试;此后用户任何目标增删改会把 localStorage 旧目标**整表写回** config(Rust 侧是无版本整体替换),静默覆盖真实配置;对空 targets 的跳过保护还会让已删目标复活。
- **修**:同步失败退避重试;保存带版本/比对;空列表保护语义修正。

---

## 二、中危(计划内修复)

| # | 问题 | 位置 | 要点 |
|---|---|---|---|
| M1 | provider suspended 后 restart_provider 无法解除,"重启服务"无效 | provider.rs:97-105,156-160 | restart 处显式重置 suspended/失败计数 |
| M2 | >1MB 服务响应静默截断且管道永久失步(此后每请求读到残段) | hardware_service.rs:145-157,240-247 | 循环读 ERROR_MORE_DATA,或解析失败一律回收连接 |
| M3 | 服务生命周期:宿主被杀留孤儿 service+provider;stop 需再有一次连接才能被观察到;join 卡在阻塞 session | main.rs accept 循环 | Job Object(KILL_ON_JOB_CLOSE)包裹 spawn;overlapped accept 或 stop 探测 |
| M4 | session 线程无上限,JoinHandle 无限累积(恶意客户端可线程爆炸) | main.rs:134,185-199 | 并发上限(如 16);完成句柄回收 |
| M5 | update_overlay_settings 无条件清空 delta_password_maps、按 20 项白名单过滤显示项 → 三角洲密码地图选择是空操作 | core.ts:73;OverlayPanelPage:682 | 透传 maps;对齐三份 id 清单 |
| M6 | config.update 桌面端不落盘,采样设置 UI 是"谎言" | state.ts:254-261 | 增加 config_save 后端命令或标注仅预览 |
| M7 | config.json 双宿主 RMW 丢更新;C# Normalize 回写抹掉模型外字段(如 Rust 写的 VisualEffectStrength) | backend.rs:116-148;FrameScopeConfigStore | 单一写入口/命名互斥量;C# 透传未知字段 |
| M8 | Rust 临时文件名按 pid,并发 targets_save 互相践踏 → 半截 JSON 被解析失败回退默认配置 | backend.rs:108 | 临时名加线程 id/UUID;写路径串行化 |
| M9 | MergeHardwareOverlaySamples O(N×M):2h 采集 ≈5200 万次比较,报告生成卡数十秒 | SystemData.cs:61-74 | 双列表有序,双指针归并 O(N+M) |
| M10 | Recorder Stop 的 Join(5000) < 最坏单次迭代(≈6s);超时后线程继续写 EndTime 之后的行 | Client.cs:48-79,152-171 | 迭代整体 deadline;done 状态记 HardwareSampleCount |
| M11 | monitor_status 对账把"后端 idle"映射为 completed——watcher 异常死亡时无报告却显示已完成 | state.ts:137-141 | idle→completed 前校验 reports_list 新增,否则 error |
| M12 | monitor.start 忽略 alreadyRunning:应用重启后 watcher 在采 A,用户启动 B → UI 声称采 B 实际采 A,停止会杀 A | state.ts:238-239;backend.rs:315-322 | alreadyRunning 时对齐/提示;后端返回当前 Game |
| M13 | OverlayPanelPage `?? prev` 粘性合并 → 后端返回 null 永远显示旧值(FPS 卡死);requestId 收集未用 | OverlayPanelPage.tsx:366-412 | null 直显 "--";乱序保护 |
| M14 | 传感器兜底匹配系统性误匹配:CPU 风扇可落机箱风扇、GPU 主温度可能取显存温度、多 GPU 按 index 配对张冠李戴 | overlay.rs:107-130,168-176;lib.rs merge_live_into_static | 取消任意第一兜底(fail-closed);量纲合理性检查(电压 0.1-2V 之外丢弃) |
| M15 | provider kill 绕过 Computer.Close(写 exit 后立即 kill),优雅退出是死代码 | provider.rs:198-205 | exit 后先 wait 3s 再 kill |
| M16 | Deploy-PreviewBackend 对在用 exe 直接强拷,IOException 后半更新状态无回滚(已实证) | Deploy-PreviewBackend.ps1:9,30-37 | 部署前检测/终止 framescope 进程;*.new 原子替换 |
| M17 | build.ps1 部分失败留脏状态;根目录 exe 版本漂移已实证(HardwareService 01:04 vs 其余 01:44);provider 锁 DLL 使依赖还原失败 | build.ps1:206-216,472-495,604-618 | obj\stage 统一构建原子替换;构建前终止 framescope 进程;distBackup 清理失败降级警告 |
| M18 | 竖排窗数值直通长小数(72.4375)与格式化混用;TargetsPage 进程下拉 key=process 重复+丢 displayText | VerticalOverlayPage.tsx:163-207;TargetsPage.tsx:74-75 | 统一 formatMetric;key 用 processId、显示 displayText |

---

## 三、低危与加固(排期消化)

**Rust 服务/宿主**
- ping.rs 每 2 秒 WSAStartup 从不 Cleanup(4.3 万次/天引用计数)
- game_fps WinEventHook 注册在无消息泵线程,回调永不触发(死代码,仅 500ms 轮询兜底)
- FPS_STARTED 一次性闩锁:非管理员首败后永不重试
- FPS 过期归零不清 1%Low/0.1%Low(叠加 H5 长期滞留)
- log crate 未初始化,ETW/权限等诊断输出全部丢弃
- `format_unix_local` 按 UTC 格式化本地时间戳 → 报告回退路径 createdAt 慢 8 小时
- parse_run_timestamp 依赖 `-p<pid>-<hash>` 尾缀;DST 歧义时 elapsed 落 0
- shell_open 经 `cmd /C start`:无空格路径含 `&`/`^` 可被 cmd 二次解析(本地注入面)
- pawnio PowerShell 单引号拼接:路径含 `'` 失败/注入面;UAC -Wait 无超时(并入 H6)
- backend_root 环境变量/祖先目录可被劫持(本地威胁模型,低)
- 命名管道默认 DACL:同用户任意进程可发 stop/clear_cache/set_sampling_interval
- 1MB 零初始化缓冲每请求分配;get_overlay_metrics 每次全树遍历+整读大 CSV(需 mtime 缓存与上限)
- disk_status 仅系统盘,与悬浮框"硬盘"项(任一盘温度)语义不一致
- 客户端不校验响应 id、无握手;provider stderr 日志固定路径互相覆盖(加 PID 后缀)
- set_sampling_interval 改全局 throttle,单客户端可影响所有客户端
- strip_exe_suffix 字节切片在极端 UTF-8 下 panic(ETW 回调内 = abort,概率极低)
- GDI 悬浮窗拖动位置不落盘(POSITION_CHANGED 死标志);hardware_service spawn 错误被吞 + check-then-act 竞态
- overlay_panel GDI VRAM 曾双重换算已修(b0c68b0);竖排窗 `?? prev` 粘性与 M13 同源

**前端**
- OverlayPanelPage 硬件轮询 requestId 未用(乱序覆盖);HardwarePage 2s 轮询无重叠保护
- HomePage 硬件分区 key=index;SensorMonitorPage 分组 key 含索引(状态错位)
- 状态总线接收无 schema 校验(旧 v1 结构整体换入)
- `target-${Date.now()}` 同毫秒撞 id;targets_get 的 count 字段前端零消费;report processName 恒空串
- ReportsPage `?id=` 指向未同步报告时静默失配
- config 双重校验规则不一致;设置保存先本地后 native,native 拒绝不回滚
- 浏览器模式 reports[].samples 无界增长可撑爆 localStorage(静默回退 initial)
- 硬件加载失败永远显示"加载中"无重试(app-startup-context 置 null)
- 桌面端 localStorage 旧样本报告在首次同步前无"样本数据"角标
- phase 死分支(stopping/generating 永不对账命中);busy 判定四处重复散比
- 被静默吞噬的错误清单:H2 processes_refresh、monitor_status 全吞、reports_list/targets_get 仅 console、app-startup 置 null——其中两处属"必须 surfaced"级

**C#/构建链**
- status.json/summary.json 不记录 hardware-samples 证据(recorder 静默失效不可诊断;Classify 不涉及)
- EnsureServiceRunning 无冷却:服务反复启动失败时每秒 spawn 一个短命进程
- 管道读超时后 dispose 阻塞中的 ReadLine 可能滞留线程池线程
- ServeMain openTask 超时后 computer 赋值无同步(CLR 强内存模型下实践安全,建议 volatile)
- RunRetention 有一盲区:worker 被杀后 status 停在 capturing 的 run 需 watcher 重启才清理(可接受)
- Run-Frontend.ps1:codex 缓存路径优先于 PATH;Node 无版本下限校验;用户指定无效路径静默落入候选

---

## 四、核实为"非缺陷"的项(避免重复排查)

- RunRetention **不会**删除正在写的 hardware-samples.csv(活跃 run 豁免 + 每目标最新豁免 + status.json 每 10s 刷新 mtime,三重保护)
- waiting-for-target 阶段 recorder 不运行(在目标确认、子进程启动之后才创建);error/timeout 路径 recorder 不停但进程随即退出,影响限于撕裂末行(报告 TryParseDate 容错)
- File.AppendAllText 每秒开关:1 次/秒开销可忽略;单写者无锁竞争;报告读取在 worker 退出后
- recorder 1 秒粒度达标(服务端 throttle 1000ms;服务不可达时退化为 2-3s 空转、行缺失而非错值)
- CSV 体积:约 130B/行,24h ≈ 11MB,远低于 MaxLogDiskMb=100 预算
- provider kill/restart 与并发 read 无竞态(全部串行在同一 Mutex;kill 内 wait 及时收尸,无僵尸)
- 服务端管道句柄各失败路径恰好一次 Disconnect+Close,无双重关闭/泄漏
- AtomicBool/Ordering 使用正确(Relaxed 均无序依赖);ERROR_PIPE_CONNECTED 竞态、BOM/\0 清洗、id 回显处理正确
- PresentMon(活跃 CSV 全共享位读)与报告读取(采集后独立进程)无锁冲突;Rust overlay-metrics 读已完成 CSV 无冲突
- hardware-samples CSV 无注入面(写出端数值列来自服务端 f64;读端引号感知 + 非数字过滤)
- 前端定时器/监听清理完整(StrictMode 双挂载有 users 引用计数;各页可见性暂停逻辑齐全)
- monitor.samples 有界(≤3600,每轮清空)

---

## 五、建议修复顺序

1. **用户可见功能断裂**(约半天):H1 report_open 可达性、H2 processes_refresh 可达性、H3 列名统一(约 3 行改动)+ 命令表一致性静态测试
2. **正确性**(约 1 天):H5 缓存 TTL + M13 粘性合并 + M11 completed 误报 + M14 兜底匹配收紧 + FPS 过期清 lows
3. **冻结与并发**(1-2 天):H6/H7 主线程与锁重构、M1 suspended 重置、M2 截断/失步、M8 临时文件、M10 Join 预算、M12 alreadyRunning
4. **生命周期与部署**(约 1 天):H4 FIRST_PIPE_INSTANCE、M3 Job Object、M4 线程上限、M15 优雅退出、M16/M17 部署与构建原子化
5. **契约完善**(迭代):H8 targets 同步/保存、M5 delta_password_maps、M6 config 落盘、M7 config 单写入口、M9 归并优化、M18 格式化统一
6. **加固排期**(低):管道 ACL、cmd start/PowerShell 引号、backend_root 校验、可观测性补全、其余低危
