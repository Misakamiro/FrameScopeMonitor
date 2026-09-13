# 会话交接文档：竖排 OSD 透明度总决算 + 样式切换三连修 + 自研安装器

**会话日期**: 2026-09-11 ~ 2026-09-13
**会话提交**: `2e6c554` → `3fc3276` → `6dd2cf3` → `9aa1be5` → `c7c05ae` → `e7d0353` → `91579d4` → `c842a56`（共 8 笔，均在 `codex/nexbox-frontend-preview` 分支，仅本地未 push）
**状态**: ✅ 全部完成并实机验证。权威架构与铁律以 `HANDOFF.md`（Round 4/5/6 章节）为准，本文档是本会话的索引与来龙去脉。

---

## 一、本会话完成了什么

### Round 4（2e6c554）——竖排 OSD 透明度总决算 ✅
用户拍板"回到方案 C 并修好三个毛病"（文字变淡/梯度窄方向反/拖动失效）。**最终架构四层缺一不可**：

1. **底板归 DOM**：`rgba(17,17,17,opacity/255)`，0%=纯文字+描边、100%=实心黑板；窗口层禁用一切 LWA_ALPHA/COLORKEY/blur-behind。
2. **vendored tao 补丁**（`desktop-preview/src-tauri/vendor/tao`，Cargo.toml `[patch.crates-io]`，`FS_TAO_LEGACY_BLUR=1` 逃生门）：transparent 窗口改走 `WS_EX_NOREDIRECTIONBITMAP`，替代上游 DwmEnableBlurBehindWindow 空区域技巧（Win11 24H2 画常驻 ~18% 黑涂层）。
3. **NRB 窗口必须 `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE)`**——否则 DWM 默认系统背板再画 ~11% 黑纱（最后一层纱的真凶）。
4. **穿透=分层对** `WS_EX_LAYERED|WS_EX_TRANSPARENT + LWA_ALPHA(255)`（单独 WS_EX_TRANSPARENT 挡不住 WebView2 子 HWND 收输入，同 Wails#5812）；交互模式两价位全剥。

三症状根因：e1aedfd 删了 DOM 底板 + 窗口层三层涂层压扁动态范围；拖动失效 = click_through 契约写反 + 穿透机制本身无效。验证：白底五档像素 255/195/136/77/17 严格线性、拖动/穿透/落盘端到端、最终无 CDP 二进制真实 UI 全链路。失败会话提交全部打书签存档（`keep/osd-experiments`、`keep/plan-d-2badfcf`、`keep/failed-session`），不混用。

### Round 5（3fc3276）——样式切换三连修 ✅
用户实测三个问题，一条根因链 + 两个独立 bug：

1. **切样式卡死/开关失效/OSD 不出现** = **同线程锁重入死锁**：start_overlay 内联 `&CURRENT_HARDWARE_DATA.lock().clone()` 传参给 draw，MutexGuard 临时值活到语句尾，draw 内 build_display_items 的 **"time" 分支经 get_net_offset_ms 重入同一锁**。worker 卡死且持有 OVERLAY_OPERATION 锁 → 一切 start/stop/热键挂起；GDI 条停在 layered 全透明初态。触发条件=时间项启用+GDI 样式（竖排不走该路径故长期潜伏）。修复=data 先 clone 到 owned 局部。**附带**：start/stop_overlay_panel 原是 spawn_job 即发即忘，invoke 恒 null，前端开关永不更新 → 改线程+channel 同步等（10s 超时）；GDI 条宽度 clamp 主屏（竖排配置喂水平条曾算出 7865px 跨双屏）。
2. **GDI 透明度闪烁** = update_settings 每滑块 tick 销毁重建窗口。修复：update_settings 不再自动重启（调用方按新样式 start_selected）+ only-opacity 热路径（WM_APP+7 原地重绘，repaint_overlay 与 WM_TIMER 共用）。
3. **托盘图标糊** = 256px 直接缩 16px。修复：预生成 tray-{16,20,24,32,48}.png（HighQualityBicubic）+ 运行时按 SM_CXSMICON 选档。

### Round 5a（6dd2cf3）——热路径补丁 ✅
用户实测"GDI 样式调透明度没变化"：热路径初版只发重绘消息+写文件，**漏了把新设置存入 CURRENT_SETTINGS**。补 `overlay_panel::store_settings`（repaint 前调用）。白底像素验证 255→17、128→144、20→247 严格线性。

### 排版修正（e7d0353）✅
安装器 GPL 小字被 Shell 的横排 flex 挤出窗外（footer 改独立插槽归位底部居中）；卸载页勾选与按钮重叠（勾选包进 OptionArea 选项区、按钮行 marginTop 22、卡收窄 480）。

### Round 6（9aa1be5）——自研 Tauri 安装器 ✅
NSIS 贴图界面天花板低，用户选型：**全自研 Tauri + 单 exe 双模式（安装/--uninstall）+ 单页式向导 + 保留 /S、路径选择、卸载删数据、开机自启**。新建 `desktop-preview/installer/`（骨架抄 `reference/nexbox/installer/`）：

- 单页 UI 同主 UI 风格（MiSans/#98DDD0/深色玻璃卡/壁纸轮播），路径/勾选/逐文件进度事件/完成态；已装检测→升级覆盖；卸载红色皮肤+删数据勾选。
- Rust（winreg/zip/mslnk）：7 进程链 taskkill、HKLM ARP 键 `Uninstall\FrameScope`（UninstallString 指向安装目录内 `FrameScope Installer.exe --uninstall`，**install 必须 place_uninstaller 复制自身**）、开始菜单/桌面快捷方式、HKCU Run 自启、NSIS ghost 键迁移、卸载自删（隐藏 PowerShell 等待退出法）。
- 静默：`/S` + `/D=路径`，main.rs 在 webview 前分发。
- 构建：`installer/build-installer.ps1`（Package-Installer.ps1 新增 `-StageOnly`）→ pack-payload.ps1 → vite → cargo → `bundle/installer/FrameScope_<ver>_x64-setup.exe`（79.4MB）。NSIS 链路并存保留作回退。
- 排版修正见上。

### Round 6a（91579d4）——payload 布局修复 ✅
用户实测"安装后硬件信息加载失败"：pack 初版把 backend/ 目录原样内嵌，装完后硬件链在 `<dir>\backend\` 而主程序按 **exe 同级**解析后端 → 全挂。修复：pack 按 NSIS 映射平铺（backend/* 到根、tools/、Fonts/）+ 两条安装路径解包前删残留 backend/。重装实测主窗口硬件全真值。

---

## 二、本轮新增铁律（已并入 HANDOFF.md，此处为速查）

1. **MutexGuard 临时值作引用传参，被调方再锁同一 Mutex = 同线程死锁**——`f(&MUTEX.lock().clone())` 的 guard 活到语句尾；修法=先 clone 到 owned 局部。cargo/静态审计查不出，只有实机+逐步插桩能抓。
2. **热路径 = 「存内存 + 通知重绘」缺一不可**——文件写成功 ≠ 内存更新。
3. **判透明必须在亮色内容上做像素采样**（铁律 22.4 二次踩实：39% 黑板叠黑壁纸看不出）。
4. **payload/资源布局必须对齐 NSIS 映射**（backend/* 平铺到安装根，tools/、Fonts/ 同级）。
5. **include_bytes! 是构建时嵌入**——改 payload 必须重跑 cargo build 再分发，顺序错了发的是旧载荷。
6. **装完别在命令里关窗**——完成态页面是交付的一部分。
7. **install 必须把自身复制到安装目录**（place_uninstaller），否则 ARP UninstallString 悬空。
8. **含中文 ps1 必须 UTF-8 BOM**（二次踩实：Write 工具写的无 BOM 文件在 PS5.1 直接 ParserError）。
9. **tauri-build 要求 icons/icon.ico 存在**（独立 Tauri 工程构建前置）。
10. PowerShell P/Invoke FindWindowExW 偶发误报 NO_WINDOW——**枚举窗口用 EnumWindows 才可靠**。

---

## 三、当前状态

- 工作树干净，8 笔会话提交全部落地；机器上 `C:\Program Files\FrameScope\` 装有修复版且硬件数据正常。
- 产物三件套（均为最新）：`desktop-preview\src-tauri\target\release\FrameScope.exe`（主程序）、`bundle\installer\FrameScope_1.0.0_x64-setup.exe`（新安装器 79.4MB）、`bundle\nsis\FrameScope_1.0.0_x64-setup.exe`（NSIS 旧版并存作回退）。
- 安装器构建命令：`desktop-preview\installer\build-installer.ps1`（-SkipPayload 跳过重新 staging）。
- 测试基线：cargo 宿主 5/5、vitest 14/14；本轮未触碰 C#/硬件服务 crate。

---

## 四、遗留项

**安装器 P3（非 bug，打磨项）**：
- 体积优化（79.4MB vs NSIS 60.5MB，payload 可换 zstd/预压缩）
- 视觉打磨（SVG 折射玻璃、进度动画、壁纸可换）
- 集成进流水线默认产物（取代 NSIS 的正式切换，需用户拍板时机）
- 升级覆盖实测（新版盖旧版）、`/D=` 路径实测——建议用户日常升级时顺带确认

**主程序既有用户决策项（未变）**：backend_root 签名校验取舍、DPI 日常实测、BF6 实测（EA 账号错误挡主菜单，用户侧）、GDI 条拖动端到端用户确认、NSIS 链路退役时机。

**已知边角（非缺陷）**：主窗 WebView2 页面滚动后鼠标 hit-test 与视觉错位（点击落在未滚动布局，重启窗口可解）——老毛病与 overlay 无关。
