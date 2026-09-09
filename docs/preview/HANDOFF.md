# FrameScope · 交接文档

> 交接日期:2026-09-10　|　交接人:ZCode　|　状态:**Round 3 完成——用户反馈 8 项(下拉样式/设置页失效/开机动画/图标/托盘/安装包/OSD分核/审计测试)全部修复并实机验证,FrameScope 1.0.0 自包含 NSIS 安装包产出,57 笔会话提交(累计 150 含历史),工作树干净,未 push**

---

## 一、项目是什么

FrameScope Monitor 是面向 Windows 游戏玩家的本地性能监控工具。当前形态:

- **UI**:Tauri 2 窗口 + NexBox 9.5.3 前端(React 19 + Chakra),经用户验收
- **实时监控**:悬浮框(GDI 原生条 + 竖排 WebView 窗)与硬件页,数据来自常驻硬件采样链(LibreHardwareMonitor 0.9.6 + PawnIO + 常驻 ETW FPS + ICMP ping),1Hz 更新,fail-closed(缺失显示 "--")
- **采集记录**:watcher 自动发现游戏进程,拉起 worker + PresentMon + ProcessSampler + SystemSampler,游戏退出后自动生成 HTML 报告
- **反作弊韧性(本轮新增)**:PresentMon 的 ETW trace 被反作弊拒绝时(BF6/Javelin 实测),报告自动从 hardware-samples.csv 的 Fps 列(常驻 ETW FPS 的 1Hz 采样)合成帧率序列,报告升级为 full 并标注 `etw-fps-sampled`
- **数据一致性**:悬浮框快照 = hardware-samples.csv = 报告 GPU 数据源(三处同源);FPS 双源保留(悬浮框 ETW 实时值 / 报告 PresentMon 逐帧,反作弊时报告自动降级用 ETW 采样,用户拍板方案 A)

## 二、仓库与运行

- 仓库:`C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild`,分支 `codex/nexbox-frontend-preview`,**46 笔会话提交(累计 139 含历史),仅本地,未 push(用户要求)**
- 预览工程:`desktop-preview/`(NexBox 前端 + FrameScope 业务层 + Tauri 宿主)
- 后端采样服务:`src/hardware-service/`(Rust)+ `src/app/FrameScopeHardwareProvider.cs`(C# LHM,`--serve` 常驻)
- 采集链:`src/app|monitoring|reporting|core`(C#)+ `packaging/`(安装/卸载)
- 部署:`tools/Deploy-PreviewBackend.ps1`(先停 FrameScope 进程链再拷贝)
- 常规命令:前端 `npm run build` → `npx tauri build --no-bundle` → 部署脚本;C# 链 `build.ps1`(构建前自动停 FrameScope 进程);硬件服务 `cargo build --release`
- 测试:cargo 20/20(服务)+ 5/5(宿主)、vitest 14/14(含 source-boundaries 命令表/列名一致性)、C# 28/28

## 三、架构速览

```
前端 React(WebView2,纯视图)
  │ invoke(core.ts:桌面→preview_native 宿主命令 / 浏览器→fixtures 模拟)
Tauri 宿主(Rust:preview_native 单命令白名单 + poller + 托盘热键)
  ├─ hardware_service.rs  管道客户端(快慢路径、锁内快请求、读看门狗 12s、DACL 管道)
  ├─ backend.rs           采集管线契约(config/targets/monitor/reports/config_save)
  ├─ overlay_panel.rs     GDI 悬浮窗(NexBox 移植,拖动位置落盘)
  └─ pawnio.rs            PawnIO 检测/安装(装卸期间 poller 暂停)
FrameScopeHardwareService(Rust,命名管道 DACL 限提权、多实例 accept、常驻)
  └─ 常驻 FrameScopeHardwareProvider.exe --serve(C#,LHM)
采集链(C#:watcher→worker→PresentMon+双 sampler→ReportGenerator)
```

关键设计:**悬浮框显示的硬件快照 = 采集期间逐秒落盘的 hardware-samples.csv = 报告 GPU 数据源**(三处同源);FPS 双源保留(悬浮框 ETW 实时 / 报告 PresentMon 逐帧;反作弊拒 PresentMon 时报告自动用 ETW 1Hz 采样合成)。

## 四、架构铁律(违反即出事,全部有前科)

1. **`csp` 必须为 null**——任何 style-src 限制经 Tauri nonce 化杀死 Emotion,UI 退化为无样式
2. **Tauri 命令处理器内不能建窗**——WebView2 控制器初始化需要消息泵,死锁;长操作用 async 命令或 spawn_job
3. **改前端必须 `npm run build` → `tauri build --no-bundle` → 部署**——dist 内嵌 exe,漏跑 = 旧界面;**tauri build 在 exe 被占用时 vite 成功但链接失败且整链 exit 0——构建后必须核验 exe 时间戳**
4. **Git Bash 传 `/x` 开头参数会被改写**——csc/taskkill 等路径参数用反斜杠,或经 python/文件方式调用
5. **老 csc 4.0.30319 只支持 C#5**——禁 is-pattern/out-var/字符串插值/`using` 嵌套简写/`when` 过滤器/**自动属性初始化器(`= ""` 也是 C#6)**;方法组 `StartNew` 有 Action/Func<T> 歧义
6. **覆盖 Windows manifest 必须保留 Tauri 默认 Common-Controls dependency,且不可加自身 assemblyIdentity**——否则 SxS 失败/进程挂死
7. **CREATE_NO_WINDOW 下 .NET 的 Console 编码设置器会崩**——provider 用 raw UTF-8 流
8. **服务命名管道多实例 accept,裸指针 handle 进闭包过不了 Send 分析——用 usize 传地址**
9. **mpsc worker 发回 `Result` 时,`recv()?` 剥的是信封,内层 Result 必须再解一次**——曾把 `{"Ok":...}` 发给前端炸 React
10. **悬浮框数据缓存 carry-forward 有 5 秒 TTL**——修改缓存逻辑时不得移除
11. **传感器匹配无"任意第一"兜底**(提示列表顺序即优先级,ranked 选择)——防机箱风扇冒充 CPU 风扇
12. **Rust 写 C# 要读的 JSON:int 字段必须写整数**——`1000.0` 浮点会让 JavaScriptSerializer 反序列化整体抛异常,ConfigStore.Load **静默回退默认配置**(watcher 全盲;config_save 曾因此让用户目标全丢)
13. **FIRST_PIPE_INSTANCE 真值是 `0x00080000`**(写成 `0x0005_0000` 会 ERROR_INVALID_PARAMETER 服务秒死),且**只允许首实例断言**——accept 循环重建第二实例时带它必得 ACCESS_DENIED 被误判"冗余"自杀
14. **硬件采样证据(HardwareSampleRows)必须在 done 状态写之后合并**——finalizing/done 会整体重写 status.json,早写被覆盖
15. **桌面模式前端 tick 不跑 fixture 采集状态机**(state.ts 有 isDesktop 守卫)——对账失败一次就会插入模拟报告
16. **前端 targets 同步:成功才置闩 + 未同步禁止整表回写**——否则陈旧 localStorage 覆盖真实配置(H8)
17. **status.json 的 finalizing/done 会整体重写**——追加字段必须在其后合并(硬件采样证据即此)
18. **反作弊(如 BF6/Javelin)会拒绝 PresentMon 的按次 ETW trace,但常驻 ETW FPS 会话不受影响**——报告自动降级 Fps 合成(etw-fps-sampled),这是边界不是 bug
19. **windows-sys 0.59 feature 门控**:OpenTraceW 在 `Win32_System_Time`、CreateFileW 在 `Win32_Security`、ReadFile 在 `Win32_System_IO`、CancelIoEx 在 `Win32_System_IO`、ConvertStringSecurityDescriptorToSecurityDescriptorW 在 `Win32_Security_Authorization`、JOBOBJECT 结构在 `Win32_System_Threading`
20. **本机 UI 自动化**:合成鼠标点击受前台锁定干扰不可靠,用 CDP(临时给 tauri.conf.json 加 `additionalBrowserArgs: "--remote-debugging-port=9333"`,**用完必须移除并重建**)+ `__TAURI_INTERNALS__.invoke` 直调宿主命令
21. **WebView2 同一 user-data 目录所有窗口的 additionalBrowserArgs 必须完全一致**——主窗带 --remote-debugging-port 而动态建窗不带时,第二个 WebView2 环境创建直接失败(ERROR_INVALID_STATE 0x8007139F),竖排窗/传感器窗全部开不出来。
22. **Tauri CloseRequested 里 hide() 托盘化必须同时 api.prevent_close()**——否则 handler 返回后默认关闭继续执行,窗口销毁→全应用退出(实机抓到的回归)。
22.1 **无边框窗口的透明度被 DWM 阴影吃掉**——Windows 上 undecorated 窗口默认带阴影,阴影会强制整面不透明合成背景,DOM 层再透明也到不了屏幕;竖排悬浮窗必须 .shadow(false)(上游同样如此)。另:用户滑块接近 100% 时面板本来就近乎不透明,判断透明是否生效要把窗口挪到亮色窗口上对比,黑壁纸上看不出来。
23. **强杀进程树会泄漏 ETW 内核会话**——残留 "NT Kernel Logger" 占住槽位后 PresentMon 静默零事件(Started recording 但零字节 CSV);用 logman query -ets 排查、logman stop "NT Kernel Logger" -ets 清理。另:CS2 新版启动停在静态模式选择对话框(不点击不渲染→零 present),自动化测试必须点过它;PM 2.x 只在优雅停止时落盘 CSV。
24. **tauri bundle 用 mainBinaryName 改名产物**——target/release 里仍是 crate bin 名(framescope-ui-preview.exe),安装包里才是 FrameScope.exe;校验脚本要查对文件。设置页/关闭行为桌面端唯一事实源是 framescope-config.json(config_read/config_save 双向),前端 localStorage 只是浏览器回退。

## 五、Round 3(2026-09-10)完成——用户反馈批次

1. **下拉控件统一**(378ec3c):可达页面 4 处原生 Select 全部换成玻璃 CustomSelect
2. **设置页真实化**(f53a968):config_read 回读+同步闩锁、保存 await+真实报错、draft 脏保护、桌面隐藏仅预览项、**目标扫描间隔真实生效**(C# 钳位 250-10000)
3. **品牌重做**(9ef0691):开机动画换成 FrameScope 自有简版(删逐字节照抄的 NexBox WebGL 三件套),全套图标(exe/托盘/标题栏/favicon)换 FrameScope 制式
4. **托盘真实可用**(883e71f):CloseRequested 按 CloseWindowBehavior 三态分发(ask 弹窗/minimize-to-tray 仅隐藏不停服务链/exit 退出)、托盘左键恢复、设置页关闭行为写真实配置
5. **OSD 分核心频率/电压**(0006428+024cbdf):竖排窗逐核显示;**AMD 电压用主板实测 Vcore**(LHM 的 Zen4 VID 用 SVI2 缩放偏小,与游戏加加实机对比 1.01-1.03V vs 1.023V 对齐;采集链早有同结论黑名单),Intel 保留真 per-core VID
6. **OSD 透明度真实穿透**(9c9e46f):清除 overlay 页文档背景,滑块控真实透明
7. **NSIS 安装包**(f28a3a2):FrameScope 1.0.0 自包含(后端链+PresentMon+字体+许可),品牌安装界面(欢迎图/头图/中文/许可页),tools/Package-Installer.ps1 全流程;静默装/卸+交互安装双验证,卸载零残留
8. **实机 QA**(见 IMPLEMENTATION.md Session 2026-09-10):页面走查截图、设置同步端到端、托盘三态、CS2 full 报告(118.6 FPS/12344 帧/379 硬件采样)、BF6 跳过(EA 账号错误仍挡,兜底链路上轮已验)
9. 附带修复:悬浮窗设置持久化恢复(try_load_persisted_settings 从未被调用)、GDI 窗口标题去 NexBox 残留、安装器 GPL 许可随附

## 五·历史、本轮(2026-09-08/09)完成

1. **Round 2 复审**(`docs/audit/2026-09-08-full-audit-round2.md`):三路深挖 + 全量实机测试;抓到 5fb6492 引入的高危回归(R-A1/R-A3,FIRST_PIPE_INSTANCE 值错 + 循环内断言,服务 100% 起不来——静态审计与 cargo test 均漏判,实机抓获)、H8 未修、R-C12 桌面模拟机注入假报告
2. **高危全修**:H1-H8(含回归 R-A1/A2/A3)、R-C1/C12、M6 回归(JSON 浮点)、管道读看门狗、管道 DACL、证据位置
3. **中危主体全修**:M1-M18(M2 降档、M7 降档、M17 部分;详见 round2 报告修复表)
4. **反作弊 FPS 兜底**(用户需求):BF6 实测 full 报告 125.7 FPS
5. **三轮低危加固**:R-A5/A7/A8/A9/A10/A11/A12、R-B3~B8、R-C5/C6/C7/C9/C10/C11、时间格式/响应 id/缓冲复用/logger/DPI 三档实测
6. **双写退役已拍板方案 A**(PresentMon 逐帧主力 + ETW 兜底,不退役)——记录在 IMPLEMENTATION.md

## 六、已知遗留(全部需要用户拍板或人工参与,无未决 bug)

0. **BF6 实测待用户**:EA 账号错误 0:85008S 仍挡主菜单(用户侧问题);反作弊兜底链路上轮已验,本 round 未重跑
0.1 **GDI 拖动落盘端到端确认**:仍需用户真实拖动一次(上轮遗留)

1. **本地加固取舍**:backend_root 签名校验(唯一剩余代码加固项);shell_open 已改 explorer 直开、taskkill 已限部署目录、管道已加 DACL
2. **WinEventHook 已移除**(本轮完成,保留 500ms GetForegroundWindow 轮询)
3. **双写退役**:已拍板方案 A,归档
4. **DPI 缩放**:三档已自动化实测通过,日常可再留意
5. **拖动位置落盘**:端到端需用户真实拖动悬浮框一次确认(合成鼠标不触发 WM_EXITSIZEMOVE)
6. round1 低危中明确不做:状态总线 schema 校验、reports `?id=` 失配提示、Run-Frontend.ps1 工具链、disk_status 语义统一(需产品决策)

## 七、关键文件地图

| 路径 | 职责 |
|---|---|
| `desktop-preview/src/preview/runtime/` | 前端核心:core.ts 分发、state.ts 状态机(**桌面不跑 fixture 机**、targets 同步守卫)、platform.ts 双模式 |
| `desktop-preview/src-tauri/src/lib.rs` | 宿主:preview_native 路由(async)、poller(R-C5 空快照)、config_save、export 加固 |
| `desktop-preview/src-tauri/src/hardware_service.rs` | 管道客户端(快慢锁、看门狗、id 校验、MORE_DATA 循环、poller 暂停门、Job Object) |
| `desktop-preview/src-tauri/src/backend.rs` | 采集契约(targets_save 整数写、monitor_stop 树杀优先、report_regenerate 超时、frame_stats 缓存) |
| `desktop-preview/src-tauri/src/overlay_panel.rs` | GDI 悬浮窗(拖动位置落盘、swap 门、泵修复) |
| `desktop-preview/src-tauri/src/pawnio.rs` | PawnIO(装卸期间 poller 暂停、按 PID 杀) |
| `desktop-preview/src-tauri/src/delta_password.rs` | 三角洲密码(社区 API,60s 缓存) |
| `src/hardware-service/src/main.rs` | 管道服务器(DACL、FIRST_PIPE_INSTANCE 仅首实例、session 上限) |
| `src/hardware-service/src/lib.rs` | overlay TTL、FPS 重试节流、force_reset |
| `src/hardware-service/src/provider.rs` | provider 优雅退出(3s 上限)、stderr 加 PID |
| `src/hardware-service/src/overlay.rs` | 传感器 ranked 匹配、最热盘温度 |
| `src/monitoring/FrameScopeHardwareServiceClient.cs` | 管道客户端(Fps 列落盘、RowsWritten 证据、spawn 冷却、EnsureServiceRunning) |
| `src/reporting/FrameScopeReportGenerator.SystemData.cs` | 双指针归并、Fps 读取、合成帧源 |
| `src/reporting/FrameScopeReportGenerator.cs` | 反作弊帧合成钩子、元数据 |
| `src/app/FrameScopeNativeMonitor.MonitorSession*.cs` | 采集会话(done 后证据合并) |
| `src/app/FrameScopeNativeMonitor.Watcher.cs` | watcher(worker 退避) |
| `src/app/FrameScopeNativeMonitor.ProcessCleanup.cs` | 进程清理(退出含服务链) |
| `tools/Deploy-PreviewBackend.ps1` | 开发部署(停进程链→拷贝) |
| `tools/Package-Installer.ps1` | NSIS 安装包全流程(构建→staging→bundle→时间戳核验) |
| `tools/New-InstallerBitmaps.ps1` | 生成安装界面品牌位图 |
| `desktop-preview/src-tauri/installer/` | 安装向导位图与许可文本 |
| `desktop-preview/src/lib/close-behavior.ts` | 关闭行为单一事实源助手(桌面写 config) |
| `docs/audit/2026-09-08-full-audit.md` / `-round2.md` | 两轮审计报告(round2 含修复验证表与实机测试) |
| `docs/preview/IMPLEMENTATION.md` | 逐会话实施记录(本轮全部批次有条目) |

## 八、给下一个会话的建议

1. 先 `git log --oneline -30` 与 `git status` 了解现状;工作树应干净
2. 任何 UI 改动走「npm run build → tauri build --no-bundle → 部署脚本 → 实机截图」闭环;**build 后核验 exe 时间戳**(csc/链接失败曾被输出过滤掩盖)
3. 动 NexBox 上游文件(`src/pages`、`src/components`)前先 diff `reference/nexbox`,差异记入 IMPLEMENTATION.md
4. 新增宿主命令时:Rust COMMANDS + 前端 core.ts/backendOnlyCommands 三处同步——source-boundaries 测试会拦截遗漏
5. **改 config 写入(Rust 侧)或 CSV 列名时:先跑跨语言契约检查**(C# JavaScriptSerializer 对浮点/Int32 严格;列名两侧 source-boundaries 已锁)
6. 真游戏全链路已多轮验证(CS2:自动发现→逐帧/采样→报告;BF6:反作弊兜底 etw-fps-sampled);日常使用直接跑即可
7. 与用户沟通默认中文;涉及取舍(视觉、范围)先问再做;提交仅本地,不推 GitHub
