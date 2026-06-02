# FrameScope Monitor 正式图标与本机安装版验证报告

结论：PASS

验证时间：2026-05-30 07:45-07:58 Asia/Hong_Kong

## 范围

- 已给 FrameScope Monitor 增加原创正式软件图标。
- 已通过 Full quiet installer 更新本机安装版。
- 未测试 BF6，未启动真实游戏，未推 GitHub，未更新 Release。
- 未删除 `%LOCALAPPDATA%\FrameScopeMonitorData`，历史 runs/reports/config/log 保留。

## 图标资产

- PNG 源文件：`assets\icon\framescope-icon.png`
- ICO 文件：`assets\icon\framescope-icon.ico`
- ICO 尺寸：`16x16, 24x24, 32x32, 48x48, 64x64, 128x128, 256x256`
- 图标主题：性能监控 / FPS 折线 / 仪表盘，原创绘制，未使用第三方商标、游戏图标、Windows/Microsoft/Apple 图标。

## 接入位置

- `FrameScopeMonitor.exe`：`build.ps1` 使用 `/win32icon:assets\icon\framescope-icon.ico` 编译 PE 图标。
- 主窗口 / 任务栏：`FrameScopeWebHostForm.Icon` 使用 `FrameScopeAppIcon.LoadWindowIcon(...)`。
- 托盘：`NotifyIcon.Icon` 使用 `FrameScopeAppIcon.LoadTrayIcon(...)`。
- 安装器：`FrameScopeMonitor-Setup.exe` 和 `FrameScopeMonitor-Full-Setup.exe` 使用同一 `/win32icon`，安装器窗口也从自身 PE 图标加载。
- 安装 payload：`dist\FrameScopeMonitor-payload\assets\icon\framescope-icon.ico/png`。
- 卸载注册表 `DisplayIcon`：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`，文件有效且 exe 带图标。
- 快捷方式：桌面和开始菜单快捷方式 `IconLocation` 均为 `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe,0`。

## 安装前状态

- 安装目录：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
- 安装版本：`1.1.3`
- 配置文件：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\framescope-config.json`
- 用户数据目录：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData`
- DataRoot：`C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs`
- 安装前无 `FrameScopeMonitor` / `PresentMon` / sampler / report generator / Vite 残留进程；存在 2026-05-27 启动的旧 `msedgewebview2` 进程，未归属于本次 smoke。

## 构建与安装

- 前端验证：`tools\Run-Frontend.ps1 verify` 通过；TypeScript 通过，Vitest `56 passed`，Vite production build 通过。
- Native 构建：`build.ps1` 通过。
- C# 测试构建：`tests\Build-FrameScopeTests.ps1` 通过。
- C# 全量测试：`tests\*Tests.exe` 全部通过，包括新增 `FrameScopeIconTests.exe`。
- 报告图表回归：`tests\chart-sampling-tests.js` 通过。
- 安装方式：`dist\FrameScopeMonitor-Full-Setup.exe /quiet`，不是 payload copy。
- quiet installer 输出 `SUCCESS`，`install.log` 记录 `2026-05-30T07:45:57` 和 `2026-05-30T07:46:10` 两次 `install-complete`。

## SHA256

- `dist\FrameScopeMonitor-Full-Setup.exe`：`252B8A42EEE4EB35868AB0F8D47E6CE951AF1E477145511C6ECE50FC1EF483BD`
- `dist\FrameScopeMonitor-Setup.exe`：`41F09A131209E04C7C73EAD7779004C49927E491763C5B47AFA98D2832379E99`
- payload `FrameScopeMonitor.exe`：`239F95314FE5CDF55AD8228D4D0853F8CC0AFB9CE2F8000E6E3E4FB0B804383F`
- installed `FrameScopeMonitor.exe`：`239F95314FE5CDF55AD8228D4D0853F8CC0AFB9CE2F8000E6E3E4FB0B804383F`
- payload/installed `assets\icon\framescope-icon.ico`：`3E1C4B249F7B66C9B1AFCE571A9494AA405ECA96AFEB8CCF11E73DE302443603`
- payload/installed `assets\icon\framescope-icon.png`：`63BE6537F96C4E20A9CEF7B1C34934CD76DAAD402FBB1EA505B34AAF6A29AACB`
- payload/installed 关键文件 hash 均匹配：`FrameScopeMonitor.exe`、`FrameScopeProcessSampler.exe`、`FrameScopeSystemSampler.exe`、`FrameScopeReportGenerator.exe`、`FrameScopeUninstaller.exe`、图标 PNG/ICO、`frontend\index.html`。

## 安装后验证

- 注册表：
  - `DisplayVersion=1.1.3`
  - `InstallLocation=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor`
  - `DisplayIcon=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\FrameScopeMonitor.exe`
  - `DataRoot=C:\Users\misakamiro\AppData\Local\FrameScopeMonitorData\framescope-runs`
- exe 关联图标：`Icon.ExtractAssociatedIcon(FrameScopeMonitor.exe)` 返回 `32x32`，不是空白默认缺失。
- 安装版启动：installer 自动启动安装版；随后 smoke 使用安装目录 exe 验证。
- WebView2 React 默认入口：live smoke 证据 `usingReactFrontend=true`，`frontendPath=C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\frontend`。
- Settings 正常：live smoke 加载 Settings，执行全局采样间隔 dirty/save/restore，`configSaveSuccessObserved=true`。
- 打开日志目录正常：live smoke `logsOpenDirectoryOk=true`，目录为 `C:\Users\misakamiro\AppData\Local\FrameScopeMonitor\`。
- 报告操作正常：live smoke 对历史 Counter-Strike-2 run 执行 reports.list/open/openDirectory/regenerate，全部成功。
- raw/FPS 下拉不回退：`chart-sampling-tests.js` 通过；安装版重新生成的报告仍含 FPS metric 选择与原始 PresentMon 帧行绘制逻辑。
- CPU Core VID 图不回退：安装版重新生成的报告 HTML/数据含 `cpuVid` 字段与 `CPU Core VID` 图表逻辑。
- WebView2 live smoke：`installed-live-smoke.json`，`success=true`，`pageLoaded=true`，`pageReady=true`，`elapsedMs=8009`。
- WebView2 reduced-motion smoke：`installed-reduced-motion-smoke.json`，`success=true`，`reducedMotion=true`，`elapsedMs=7135`。
- 托盘 smoke：`installed-tray-smoke.json`，`success=true`，`initialTrayVisible=true`，`duplicateTrayIconsPrevented=true`，窗口隐藏/恢复/退出守卫均通过。

## 用户数据保留

- `%LOCALAPPDATA%\FrameScopeMonitorData` 未删除。
- 当前统计：`framescope-runs` 下 41 个 run 目录，`diagnostic-reports` 下 237 个诊断目录。
- 本次 live smoke 会生成新的诊断报告并重生成一个历史报告用于功能验证，但没有清空历史 runs/reports/config/log。

## 残留进程

- 最终检查无 `FrameScopeMonitor`、`PresentMon`、`FrameScopeProcessSampler`、`FrameScopeSystemSampler`、`FrameScopeReportGenerator`、`vite` 残留。
- 仍存在 2026-05-27 启动的旧 `msedgewebview2` 进程；这些在本次任务开始前已存在。smoke 启动的 Edge/外部进程已由 smoke cleanup 清理。

## 其他检查

- `git diff --check`：通过；仅输出现有工作区 LF/CRLF warning，无 whitespace error。
- 工作区在本任务前已有大量未提交改动；本次只新增/修改图标、图标接入、图标测试和本报告相关内容，没有回退既有改动。

## 建议

- 可以进入最终打包验证。
- 不建议在当前任务内推 GitHub 或更新 Release；本轮已按要求未执行 GitHub push / Release 更新。
