# FrameScope · 交接文档

> 交接日期：2026-09-08　|　交接人：ZCode　|　状态：**前端预览、后端迁移(M0-M4)、数据一致性改造、全项目审计与修复——全部完成并实机验证**

---

## 一、项目是什么

FrameScope Monitor 是面向 Windows 游戏玩家的本地性能监控工具。当前形态：

- **UI**：Tauri 2 窗口 + NexBox 9.5.3 前端(React 19 + Chakra,复制后叠加 FrameScope 业务层)，经用户验收
- **实时监控**：悬浮框(GDI 原生条 + 竖排 WebView 窗)与硬件页/传感器窗，数据来自常驻硬件采样链(LibreHardwareMonitor 0.9.6 + PawnIO + ETW FPS + ICMP ping),1Hz 更新,全部真实数据、fail-closed(缺失显示 "--",不伪造)
- **采集记录**：用户选择监控目标启动后，watcher 自动发现游戏进程，拉起 worker + PresentMon(帧)+ ProcessSampler + SystemSampler,游戏退出后自动生成交互式 HTML 报告
- **数据一致性**：报告的 GPU 序列来自与悬浮框同源的硬件快照(`hardware-samples.csv`),报告新增 CPU 温度/功耗/电压/ping 图表

## 二、仓库与运行

- 仓库:`C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild`,分支 `codex/nexbox-frontend-preview`,**21+ 笔本地提交,GitHub 未推**(用户拍板仅本地)
- 预览工程:`desktop-preview/`(NexBox 前端 + FrameScope 业务层 + Tauri 宿主)
- 后端采样服务:`src/hardware-service/`(Rust)+ `src/app/FrameScopeHardwareProvider.cs`(C# LHM,`--serve` 常驻)
- 采集链:`src/app|monitoring|reporting|core`(C#,watcher→worker→PresentMon/samplers→ReportGenerator)
- 部署:`tools/Deploy-PreviewBackend.ps1` 把后端链部署到 `desktop-preview/src-tauri/target/release/`(预览 exe 旁,自包含;脚本会先停运行中的 FrameScope 进程)
- 常规命令：前端 `npm run build` → `npx tauri build --no-bundle` → 部署脚本；C# 链 `build.ps1` 全量；硬件服务 `cargo build --release`
- 测试:硬件服务 cargo test 19、宿主 5、前端 vitest 13(含 source-boundaries 命令表一致性测试)

## 三、架构速览

```
前端 React(WebView2,纯视图)
  │ invoke(core.ts:桌面→preview_native 宿主命令 / 浏览器→fixtures 模拟)
Tauri 宿主(Rust)
  ├─ hardware_service.rs  管道客户端(快慢路径,锁不跨 I/O)
  ├─ backend.rs           采集管线契约(config/targets/monitor/reports)
  ├─ overlay_panel.rs     GDI 悬浮窗(NexBox 移植)
  └─ pawnio.rs            PawnIO 检测/安装
FrameScopeHardwareService(Rust,命名管道多实例 accept,常驻)
  └─ 常驻 FrameScopeHardwareProvider.exe --serve(C#,LHM,raw UTF-8 流)
采集链(C#,watcher 常驻发现目标→worker→PresentMon+双 sampler→报告)
```

关键设计:**悬浮框显示的硬件快照 = 采集期间逐秒落盘的 hardware-samples.csv = 报告 GPU 数据源**(三处同源);FPS 双源保留(悬浮框 ETW 实时值 / 报告 PresentMon 逐帧,用户拍板)。

## 四、架构铁律(违反即出事,全部有前科)

1. **`csp` 必须为 null**——任何 style-src 限制经 Tauri nonce 化杀死 Emotion,UI 退化为无样式
2. **Tauri 命令处理器内不能建窗**——WebView2 控制器初始化需要消息泵,死锁;长操作用 async 命令或 spawn_job
3. **改前端必须 `npm run build` → `tauri build --no-bundle` → 部署**——dist 内嵌 exe,漏跑 = 旧界面;部署前跑 Deploy 脚本(会停运行中的进程,防半更新)
4. **Git Bash 传 `/x` 开头参数会被改写**——csc 等路径参数用反斜杠
5. **老 csc 4.0.30319 只支持 C#5**——禁 is-pattern/out-var/字符串插值/`using` 嵌套简写/`when` 过滤器;方法组 `StartNew` 有 Action/Func<T> 歧义
6. **覆盖 Windows manifest 必须保留 Tauri 默认 Common-Controls dependency,且不可加自身 assemblyIdentity**——否则 SxS 失败/进程挂死 4.6MB
7. **CREATE_NO_WINDOW 下 .NET 的 Console 编码设置器会崩**——provider 用 raw UTF-8 流
8. **服务命名管道多实例 accept,裸指针 handle 进闭包过不了 Send 分析——用 usize 传地址**
9. **mpsc worker 发回 `Result` 时,`recv()?` 剥的是信封,内层 Result 必须再解一次**——曾把 `{"Ok":...}` 发给前端炸 React(PawnIO 黑屏事件)
10. **悬浮框数据缓存 carry-forward 有 5 秒 TTL**——修改缓存逻辑时不得移除,否则过期值永久冒充当前值
11. **传感器匹配无"任意第一"兜底**(必须名称提示命中)——防机箱风扇冒充 CPU 风扇
12. `windows-sys 0.59` feature 门控:OpenTraceW 在 `Win32_System_Time`、CreateFileW 在 `Win32_Security`、ReadFile 在 `Win32_System_IO`、GENERIC_READ 在 `Foundation`
13. **watcher 是常驻进程**,采集完成后继续轮询新目标——常见进程(如 cmd.exe)做目标会被反复捕获

## 五、本次会话(2026-09-07/08)完成

1. **M0-M4 后端迁移**(详见 git log 与 IMPLEMENTATION.md):硬件采样链落位、常驻采样核心、宿主接桥、采集报告链接管、管理员 manifest、PawnIO、CS2 真游戏端到端验证
2. **数据一致性改造**:采集期间落盘悬浮框同源快照(hardware-samples.csv,16 列)、报告 GPU 数据消费同源 CSV、硬件服务多客户端
3. **全项目审计**(docs/audit/2026-09-08-full-audit.md):三路并行深挖 + 人工核实,高危 8/中危 ~20/低危 ~25
4. **审计修复**:全部高危与绝大多数中危已修(见 git log 28fb00d/3fcea46/8611ecc 等),含报告按钮可达性、GPU 合并列名、缓存 TTL、传感器匹配收紧、主线程阻塞、provider 优雅退出、部署原子性

## 六、已知遗留(低优先级,不阻塞)

- **DPI 缩放**：100/125/150% 实测留给日常使用(声明 PerMonitorV2 已做)
- **SMART 直读精化、NVML 双源 GPU 合并**:LHM 值已覆盖同等数据,属锦上添花
- **审计低危/加固项**:管道 ACL、`cmd /C start` 与 PowerShell 引号注入面收窄、backend_root 签名校验、get_overlay_metrics 大 CSV 缓存、log crate 初始化、报告 HTML 新增 CPU 温度/ping 图表展示(hardware-samples 数据已进 data.js 管线)、SystemSampler/PresentMon 双写退役决策
- **watcher 常驻特性**:常见进程做目标会被反复捕获(设计使然);测试目标 cmd.exe 已停用,真游戏目标按需启用
- 竖排悬浮窗"时间"项固定北京时间(NexBox 上游行为,国内产品语义下合理)

## 七、关键文件地图

| 路径 | 职责 |
|---|---|
| `desktop-preview/src/preview/runtime/` | 前端核心:core.ts 命令分发、state.ts 状态机与后端对账、platform.ts 双模式 |
| `desktop-preview/src-tauri/src/lib.rs` | 宿主命令路由(async)+ poller + 托盘热键 |
| `desktop-preview/src-tauri/src/hardware_service.rs` | 硬件服务管道客户端 |
| `desktop-preview/src-tauri/src/backend.rs` | 采集管线契约(targets/monitor/reports/processes) |
| `desktop-preview/src-tauri/src/overlay_panel.rs` | GDI 悬浮窗(NexBox 移植) |
| `desktop-preview/src-tauri/src/pawnio.rs` | PawnIO 检测/安装/卸载 |
| `src/hardware-service/src/` | 采样服务(main 多实例 accept/lib 协议+缓存/provider 常驻管理/overlay 聚合/ping/game_fps ETW) |
| `src/app/FrameScopeHardwareProvider.cs` | LHM 常驻 provider(--serve) |
| `src/monitoring/FrameScopeHardwareServiceClient.cs` | worker 侧管道客户端 + 快照落盘器 |
| `src/monitoring/FrameScopeSystemSampler.Gpu.cs` | GPU 采样(已改从硬件服务取,无 nvidia-smi) |
| `src/reporting/FrameScopeReportGenerator.SystemData.cs` | 报告系统数据(含 hardware-samples 合并) |
| `tools/Deploy-PreviewBackend.ps1` | 后端链部署(停进程→拷贝) |
| `docs/audit/2026-09-08-full-audit.md` | 全项目审计报告 |
| `docs/preview/IMPLEMENTATION.md` | 逐会话实施记录与偏差清单 |
| `reference/nexbox/` | NexBox 冻结参考(只读) |

## 八、给下一个会话的建议

1. 先 `git log --oneline -25` 与 `git status` 了解现状;工作树应干净
2. 任何 UI 改动走「npm run build → tauri build --no-bundle → Deploy 脚本 → 实机截图」闭环
3. 动 NexBox 上游文件(`src/pages`、`src/components`)前先 diff `reference/nexbox`,差异记入 IMPLEMENTATION.md
4. 新增宿主命令时:Rust COMMANDS + 前端 backendInvoke(或 nativeCommands)三处同步——source-boundaries 一致性测试会拦截遗漏
5. 真游戏全链路已验证(CS2):目标自动发现 → 30952 帧录制 → 报告自动生成(FPS 119.6);日常使用直接跑即可
6. 与用户沟通默认中文;涉及取舍(视觉、范围)先问再做;提交仅本地,不推 GitHub
