# 2026-10-22：六项用户反馈的修复与验收

本报告记录本轮证据，不替代真实用户机器的登录、双显卡、代理和关机验收。基线分支 `codex/2.0`，HEAD `8959a947efe55718fa858675817ad82cd4d8904c`。保留此前 Wry 透明背景修改及 Mica For Everyone 的竖排窗口例外；本轮没有重置工作区、提交、推送或修改历史采集数据。

## 变更与证据

| 反馈 | 定位与修复 | 已验证与边界 |
| --- | --- | --- |
| 自启动开关消失 | 在实际 Tauri 前端恢复“随 Windows 启动”，通过原生命令读取 Windows 任务状态；开关立即生效。主程序有 `requireAdministrator` manifest，改用当前用户的交互式登录计划任务，最高权限执行固定程序及 `--autostart` 参数。任务按安装路径和用户 SID 隔离。 | 原生只读任务查询、管理员 TEMP 任务创建/禁用/删除/回滚通过；用户确认设置中两个开关均可见。没有自动开启正式程序的自启动。 |
| 自启动显示主界面或最小化任务栏 | 在 Tauri 创建窗口前设置 `main.visible=false`，自动启动不恢复已有实例的主窗口。旧 `StartMinimized` 配置继续兼容，界面解释改为“手动启动时也直接进入托盘”。 | 本机 `--autostart` 启动后主窗口 HWND 1181798 的可见状态为 false；用户随后确认点击托盘可正常恢复主界面。真实注销、重新登录仍需用户验收。 |
| 报告出现大量细线 | 视频中的残线位于右侧和下侧，符合 DPR 小于 1 时的部分清屏。原代码把物理画布尺寸传入已缩放的 context，再次缩小了清屏范围。现在保存变换、切回单位矩阵、清空物理像素、恢复变换。 | 修改前 DPR=.5 残留 93750 像素、.8 残留 115200 像素。修改后 .5/.8/1/1.25/2 全为 0。候选 C# 生成完整报告三件套，真实 Edge 在 .8/1.25 下连续画 99 条辅助线再移出图表，残留为 0，无页面错误。已有旧 HTML 需要重新生成。 |
| GitHub 加速慢、开启代理失效或错误 | 更新器关闭 reqwest 默认功能时遗漏了 `system-proxy`；补上 Windows 系统代理、SOCKS 和系统信任证书支持。有代理时优先原站；逐项回退，沿用成功线路，避免反复等待失败加速站。保留两条线路及连接/TLS 的错误原因；缩短连接和元数据等待，为下载加上无数据读取期限。 | 测试代理实际收到独立子进程的元数据和下载请求；线路顺序、失败后线路沿用、签名、缓存哈希、取消阻塞下载均通过。第三方加速站真实吞吐量和用户的特定代理仍待现场复测。没有关闭 TLS 或签名检查。 |
| OSD 跳到核显 | 之前按列表下标取 GPU。现在选定 LHM 设备标识；自动初选优先显存容量较大的设备，保留用户手动选择。设备换序或短暂缺失不会借用另一张卡的读数。 | 覆盖 AMD 核显+NVIDIA 独显、Intel 核显+AMD 独显、后出现独显、换序、缺失、TTL 到期和手动选核显。自动初选按容量是启发式，缺少容量或特殊 UMA 配置需手动选择；真实双显卡游戏尚待反馈机器验证。 |
| 退出/关机出现 HardwareService 0xc0000142 | 找到 Rust 主程序和 C# 记录器两条失联后拉起服务的路径。退出开始就禁止新的主程序连接/进程，已有连接继续收尾，退出失败则恢复服务可用状态。Rust 服务/provider 与 C# 客户端在 Windows 关机期间拒绝新建硬件进程。 | 退出门控、取消退出恢复、真实假管道收尾、禁止迟到重连通过。照片只证明进程初始化错误，不能据此断言所有 0xc0000142 都由此竞态引起。没有执行真实关机测试或通过隐藏错误弹窗来处理。 |

## 文件归属

- `desktop-preview/src/preview/settings/FrameScopeSettings.tsx`：Windows 自启动入口及托盘启动说明。
- `desktop-preview/src/preview/settings/UpdateSettings.tsx`：更新线路行为说明。
- `desktop-preview/src-tauri/src/lib.rs`：自启动原生命令、创建窗口前隐藏、自动启动的单实例行为、提前进入退出门控。
- `desktop-preview/src-tauri/src/hardware_service.rs`：退出和关机启动保护、取消退出恢复、保留实际 spawn 错误。
- `src/updater/src/autostart.rs`：当前用户计划任务，固定目标与参数、有限等待、真实状态回读和旧 Run 项迁移。
- `desktop-preview/installer/src-tauri/src/installation_system.rs`：安装/升级迁移、卸载清理、事务快照和回滚计划任务状态；旧事务快照兼容。
- `src/updater/Cargo.toml`、`src/updater/src/lib.rs` 及三个相关 Cargo.lock：代理、证书、逐项回退、线路记忆和错误原因。
- `src/hardware-service/src/overlay.rs`、`lib.rs`：GPU 设备选择与 TTL 一致性。
- `src/hardware-service/src/provider.rs`、`src/core/windows_shutdown.rs`、`src/monitoring/FrameScopeHardwareServiceClient.cs`：服务/provider/记录器的关机启动限制。
- `src/reporting/FrameScopeReportGenerator.Html.Scripts.cs`：辅助线画布清屏修复。
- 测试：`tests/chart-overlay-clear.browser.cjs`、`tests/FrameScopeHardwareShutdownTests.cs`、`tests/Build-FrameScopeTests.ps1`、Rust 模块内回归测试、`src/updater/examples/autostart_lifecycle.rs`。

## 自动检查

- 主程序 Rust：99 passed，12 ignored。
- 硬件服务 Rust：87 passed，3 ignored。
- 更新器 Rust：15 passed，1 ignored；另显式执行原生只读任务查询并通过。
- 安装器 Rust：55 passed，2 ignored。
- 前端：17 个测试文件、84 项通过；TypeScript 检查和 Vite 构建通过。
- C#：报告 manifest（含四类报告及发布/回滚）、报告恢复、硬件关机保护通过。
- 图表：真实 Edge 画布像素回归、候选生成的完整 HTML hover 回归、原有抽样和图例测试通过。
- ignored 项保留各自原有的设备/权限/进程隔离条件，不计入已通过数量。未为本轮无关的既有编译警告修改代码。

## 构建、包和本机更新

组件候选位于 `C:\fs-build\b203\six-issue-candidate`，沿用已有 b203 缓存，没有新增第三套完整构建。

主程序含当前前端资源，大小 57,633,792 字节。已核对 HTML 引用的 JS/CSS 资源键出现在 EXE 中，资源文件哈希保存在 `embedded-assets.json`。

工作区原有缓存 payload 比 beta.3 旧且缺 HostCapabilities，验证中拒绝使用。最终以 b203 已验证 beta.3 ZIP（SHA256 `55F3589C14488CB4F8620F249348F9FDB8C1A1CA5E5FC5C5F5DDAA8FADC14388`）为基础，替换本轮四个 EXE 和能力标记，校验全部 36 个 payload 文件。保留基础包输入清单，并用 `componentOverride` 明确记录本轮输入、替换及来源，属于本地组件候选包，不冒充全新正式发行。

新 payload SHA256：`4CD8F031A4921FD76EB93B2C087C294A951B45D9AA546ED6A8D217F4D9057F6B`。在新安装器中找到整个 ZIP 的连续字节并重新计算哈希一致；不是只核对构建命令退出码。原工作区被忽略的 payload.zip 已按备份恢复。

本机更新脚本为 `artifacts/20261022-six-issues/Install-LocalCandidate.ps1`：请求旧版正常退出，等待链路收尾，管理员 TEMP 任务验收，逐文件备份并核对前后哈希，替换四个运行 EXE、能力标记和新版安装器，再以 `--autostart` 启动。配置、历史、用户 OSD 设置和原始发行凭据不改写。第一次部署因 Windows PowerShell 5.1 对 JSON 数组的处理差异，在替换前失败；旧程序哈希未变，修正脚本后重试，第一次结果另存。

本机最终更新成功：四个运行 EXE、能力标记和新版安装器共六个文件的最终哈希均与候选一致，见 `installed-hashes-final.json`。配置、历史、OSD 设置和发行凭据五项哈希在替换前后及运行验收后保持一致，见 `protected-data-final.json`。Mica 配置仍为 SHA256 `6CE0618D4CD6F85ECFA5B502AFE5702F03896FC04EE6D694C52DCF162168A2CC`。

管理员 TEMP 任务完整验收结果为 `passed=true`，测试任务清理成功。新版本实际读取到 CPU 使用率 12.3%、频率 4950 MHz 和 NVIDIA GeForce RTX 3080。正常退出后本安装的主程序/监控/服务/provider 进程数量为 0，验收记录的 `FrameScope_FPS` 会话消失；随后重新按托盘模式启动，主程序 PID 70164、服务 PID 55492，watcher 为 idle。原生硬件管道拒绝普通权限读取，实际读数通过管理员验收获取，没有放宽管道权限。

用户确认：“主界面恢复正常，两个开关都能看到”。相关记录：`deployment-result.json`、`task-lifecycle-admin.json`、`runtime-acceptance.json`、`startup-windows.json`、`user-acceptance.json`。本机物理读取只返回一张 GPU，不能替代双显卡反馈机器的验收。

## 剩余验收和回滚

需要继续确认：实际登录启动无主窗口闪现、用户机器的双显卡游戏、真实代理与下载吞吐、发生过 0xc0000142 的机器正常退出/关机。托盘恢复已由用户验收。本机没有卸载产品来模拟首次安装，也没有执行注销或关机；首次安装、覆盖安装和两种用户数据卸载分支目前有事务测试覆盖，完整 Windows 系统副作用仍需隔离环境验收。

若本机候选已安装，正常退出后执行同一个脚本的 `-Rollback` 分支；如果手动启用了新的自启动，先在新界面关闭该开关再回滚。脚本核对当前文件和备份哈希，拒绝覆盖其他操作产生的不同版本。回滚材料目录为 `artifacts/20261022-six-issues/before-installed-candidate`。不回滚用户配置、历史或报告。

实现参考：[reqwest 0.12.28 的功能说明](https://docs.rs/reqwest/0.12.28/reqwest/#optional-features)、[Microsoft ScheduledTasks principal 参数](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/new-scheduledtaskprincipal?view=windowsserver2025-ps)。具体测试日志与哈希位于本轮 artifacts 目录。

自动安全审查拒绝了删除本轮临时构建缓存及 ZIP 解压目录的清理命令，仅返回 `blocked by policy`，未提供细分原因。因此保留这些临时目录，不改用其他方式重试删除。构建根仍只有 b202、b203，最终占用记录为 `storage-final.json`。
