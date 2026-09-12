# 竖排悬浮框透明度修复 - 交接文档

> **✅ 已解决(2026-09-11 Round 4)**:本档记录的失败会话已由后续会话纠正并完整修复,详见 [HANDOFF.md](HANDOFF.md) "Round 4" 章节与铁律 22.2-22.4。要点:741cc61 的"OSD 全坏"是 Rust(e1aedfd)/前端(a0af3dc) 混搭错配假象;最终方案=vendored tao 走 WS_EX_NOREDIRECTIONBITMAP + DWMSBT_NONE 关系统背板 + 穿透用 LAYERED|TRANSPARENT+LWA_ALPHA(255) 分层对 + 底板归 DOM rgba。本文档仅作历史存档,"六、建议后续方向"中的方案 A/B/D 均已被实测否决(方案 C 思路即最终落地的"前端自绘"方向)。

---

**会话日期**: 2026-09-11  
**任务**: 修复竖排悬浮框透明度问题  
**状态**: ~~❌ **未完成，OSD 显示功能全部异常**~~ → **已由 Round 4 解决(见顶部横幅)**

---

## 一、任务背景

用户反馈当前版本 (a0af3dc) 的竖排悬浮框透明度效果"还不如上一个版本，和原来的那些问题一样"，要求修复**方案 C** (05606fe) 存在的具体问题，而非更换透明度方案。

### 透明度方案演进历史

1. **9c9e46f** - 最初方案：简单的 `transparent(true)` + `set_ignore_cursor_events`
2. **2f51b01** - 修复 1：`shadow(false)` 移除 DWM 阴影强制的不透明底
3. **05606fe** - **方案 C**：调用 `set_ignore_cursor_events(true)` 后手动 strip `WS_EX_LAYERED`
4. **e1aedfd** - 修复方案 C 竞态：完全不调用 `set_ignore_cursor_events`，自行管理 `WS_EX_TRANSPARENT`
5. **a0af3dc** - DWM blur-behind 方案：动态控制 DWM 毛玻璃层（用户拒绝）

### 方案 C 的核心问题

根据 a0af3dc 提交信息：

> "User re-testing disproved the post-hoc LAYERED-strip patch: any set_ignore_cursor_events call replays tao's full WindowFlags ex-style asynchronously (LAYERED included), racing any post-call strip - the panel flapped between frosted and opaque."

**问题根源**：tao 的 `set_ignore_cursor_events` 会异步重放完整的 WindowFlags（包括 `WS_EX_LAYERED`），与手动 strip 操作竞态，导致透明度在毛玻璃和不透明之间闪烁。

---

## 二、本次会话操作记录

### 错误操作流程

1. **误读需求**：最初误以为用户要求完全移除 DWM blur-behind，回归纯 DOM 透明度控制
2. **113152e 提交**：删除了所有 `overlay_apply_backdrop` 调用和函数定义
3. **构建并测试**：用户反馈 **OSD 根本没有显示出来**
4. **回滚到 05606fe**：恢复方案 C 原始代码
5. **再次回滚到 e1aedfd**：恢复正确的修复方案（自行管理 `WS_EX_TRANSPARENT`）
6. **741cc61 提交**：恢复 e1aedfd 的代码

### 最终状态

- **当前 HEAD**: 741cc61 (detached HEAD state)
- **最新构建**: `FrameScope.exe` @ 2026-09-11 02:25
- **问题**: ❌ **OSD 显示功能全部异常，修改后导致 OSD 本身无法正常显示**

---

## 三、技术细节

### e1aedfd 方案的关键代码

#### 1. 自定义点击穿透函数

```rust
#[cfg(windows)]
fn overlay_set_hit_test_transparent(window: &tauri::WebviewWindow, transparent: bool) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        GetWindowLongW, SetWindowLongW, GWL_EXSTYLE, WS_EX_TRANSPARENT,
    };
    if let Ok(hwnd) = window.hwnd() {
        unsafe {
            let ex = GetWindowLongW(hwnd.0 as _, GWL_EXSTYLE);
            let mut next = ex & !(WS_EX_TRANSPARENT as i32);
            if transparent {
                next |= WS_EX_TRANSPARENT as i32;
            }
            SetWindowLongW(hwnd.0 as _, GWL_EXSTYLE, next);
        }
    }
}
```

**核心逻辑**：
- 只操作 `WS_EX_TRANSPARENT` 位
- 不触碰 `WS_EX_LAYERED`
- 完全绕过 tao 的窗口样式管理

#### 2. 窗口创建时启用点击穿透

```rust
fn open_vertical(app: &tauri::AppHandle) -> Result<(), String> {
    // ... 创建窗口 builder ...
    let window = builder.build().map_err(|e| e.to_string())?;
    overlay_set_hit_test_transparent(&window, true);  // 不再调用 set_ignore_cursor_events
    #[cfg(windows)]
    overlay_apply_backdrop(&window, settings.opacity.min(255) as u8);
    Ok(())
}
```

#### 3. 拖动模式切换

```rust
"set_overlay_drag_mode" => {
    let enabled = args["enabled"].as_bool().ok_or("enabled must be boolean")?;
    overlay_panel::set_drag_mode(enabled);
    if let Some(window) = app.get_webview_window("vertical-overlay") {
        overlay_set_hit_test_transparent(&window, !enabled);
        #[cfg(windows)]
        overlay_apply_backdrop(&window, overlay_panel::get_or_init_settings().opacity.min(255) as u8);
        app.emit("vertical-overlay-drag-mode", enabled).map_err(|e| e.to_string())?;
    }
}
```

### overlay_apply_backdrop 逻辑（e1aedfd 版本）

```rust
#[cfg(windows)]
fn overlay_apply_backdrop(window: &tauri::WebviewWindow, opacity: u8) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        GetWindowLongW, SetLayeredWindowAttributes, SetWindowLongW, GWL_EXSTYLE, LWA_COLORKEY,
        WS_EX_LAYERED,
    };
    if let Ok(hwnd) = window.hwnd() {
        unsafe {
            let ex = GetWindowLongW(hwnd.0 as _, GWL_EXSTYLE);
            if opacity == 0 {
                // 0%: colorkey mode. The DOM paints the plate pure magenta
                // (0xFF00FF) and the key makes those pixels fully invisible
                // while text/icons stay - the only state where the plate can
                // be 100% gone (DWM blur-behind always draws frost).
                SetWindowLongW(hwnd.0 as _, GWL_EXSTYLE, ex | WS_EX_LAYERED as i32);
                SetLayeredWindowAttributes(hwnd.0 as _, 0x00FF00FF, 0, LWA_COLORKEY);
            } else {
                // >0%: strip LAYERED. The DWM blur-behind (enabled at creation) gives
                // the frosted glass; the DOM tint darkens it as opacity rises.
                SetWindowLongW(hwnd.0 as _, GWL_EXSTYLE, ex & !(WS_EX_LAYERED as i32));
            }
        }
    }
}
```

**注意**：e1aedfd 版本仍使用色键方案（opacity=0 时设置洋红色键），这在 HANDOFF.md 中已标注为**不可行方案**（WebView2 是子 HWND，父窗口 LWA_COLORKEY 不处理其像素）。

---

## 四、已知问题

### 🔴 **关键问题：OSD 显示功能全部异常**

**表现**：修改透明度相关代码后，竖排悬浮框（vertical-overlay）无法正常显示

**可能原因**：
1. 误删了关键的窗口创建/显示逻辑
2. `overlay_apply_backdrop` 的色键模式（opacity=0）干扰了窗口渲染
3. `WS_EX_LAYERED` 在 opacity=0 时被添加，但与 per-pixel alpha 冲突
4. 前端 DOM 的洋红色背景（`#FF00FF`）未正确配置

### 其他已知限制

1. **色键方案不可行**：根据 HANDOFF.md 铁律 22.3，WebView2 是独立子 HWND，父窗口的 LWA_COLORKEY 不处理其像素
2. **DWM blur-behind 用户拒绝**：a0af3dc 方案在 Win11 24H2 上即使 tint alpha=0 也会产生持续毛玻璃层
3. **tao 异步竞态未彻底解决**：e1aedfd 只是绕过了 `set_ignore_cursor_events`，但 `overlay_apply_backdrop` 在 opacity>0 时仍然 strip LAYERED，可能与其他 tao 操作冲突

---

## 五、未解决的核心问题

### 用户需求

- **0% 透明度**：背景板完全消失，只显示纯白文字和图标
- **1-99% 透明度**：半透明黑色背景，平滑过渡
- **100% 透明度**：实心黑色底板

### 当前障碍

1. **WebView2 架构限制**：
   - WebView2 是独立的子 HWND，有自己的合成层
   - 父窗口的 `LWA_COLORKEY`、`LWA_ALPHA` 等分层窗口属性不影响子窗口像素
   - DWM blur-behind 在 Win11 24H2 上无法实现真正的 0% 透明（总有毛玻璃层）

2. **tao 窗口管理干扰**：
   - tao 会异步重放 WindowFlags，与手动样式修改竞态
   - 即使绕过 `set_ignore_cursor_events`，其他 tao 操作（如 resize、move）也可能触发样式重放

3. **前端 DOM 透明度无法单独工作**：
   - 单纯的 `background: rgba(17, 17, 17, 0)` 在透明窗口上确实能让背景完全透明
   - 但用户报告"还是有半透明毛砂底"，说明可能有其他层级（WebView2 合成层、DWM 合成层）干扰

---

## 六、建议后续方向

### 方案 A：深入 WebView2 合成控制

**思路**：通过 WebView2 的 `CoreWebView2Controller` 接口控制其合成模式

```rust
// 伪代码
let controller = webview.controller();
controller.set_default_background_color(Color::Transparent);
controller.set_is_visible(true);
```

**参考**：
- Microsoft Edge WebView2 文档：[CoreWebView2Controller.DefaultBackgroundColor](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2controller#defaultbackgroundcolor)
- Tauri 可能已封装相关 API

### 方案 B：使用 Acrylic 或 Mica 材质

**思路**：Win11 原生支持 Acrylic/Mica 窗口材质，可能绕过 DWM blur-behind 的毛玻璃问题

```rust
use windows::Win32::UI::WindowsAndMessaging::*;
use windows::Win32::Graphics::Dwm::*;

// 设置 Acrylic 背景
DwmSetWindowAttribute(
    hwnd,
    DWMWA_SYSTEMBACKDROP_TYPE,
    &DWMSBT_MAINWINDOW, // 或 DWMSBT_TRANSIENTWINDOW
    size_of::<u32>()
);
```

**风险**：可能仍然无法实现真正的 0% 透明

### 方案 C：前端 Electron-style Frameless + 自绘

**思路**：
1. Rust 后端只负责创建完全透明的无边框窗口（不启用 DWM 任何特效）
2. 前端 DOM 完全自绘背景板（canvas 或 CSS），包括毛玻璃效果的模拟
3. 透明度滑块直接控制 DOM 层的 alpha

**优点**：
- 完全控制渲染流程，不受 DWM/WebView2 合成层干扰
- 0% 透明度可以真正做到只显示文字

**缺点**：
- 前端需要自己实现毛玻璃效果（backdrop-filter 或 canvas blur）
- 性能可能不如原生 DWM

### 方案 D：接受 DWM 限制，调整用户预期

**思路**：
1. 在设置界面明确说明："Windows 11 系统限制，0% 透明度仍可能显示轻微背景"
2. 将滑块范围调整为 10%-100%，移除无法实现的 0% 档位
3. 10% 时配置为最浅的毛玻璃 + 极低 DOM alpha

**优点**：
- 技术实现简单，不与系统对抗
- 10%-100% 的渐变效果可以很平滑

**缺点**：
- 不满足用户"0% 纯文字"的需求

---

## 七、关键文件清单

| 文件路径 | 说明 |
|---------|------|
| `desktop-preview/src-tauri/src/lib.rs` | 主逻辑，包含 `open_vertical`、`overlay_set_hit_test_transparent`、`overlay_apply_backdrop` |
| `desktop-preview/src-tauri/src/overlay_panel.rs` | GDI 原生悬浮框逻辑（未受本次修改影响） |
| `desktop-preview/src/pages/VerticalOverlayPage.tsx` | 竖排悬浮框前端页面，第 455 行设置背景 `rgba(17, 17, 17, ${bgOpacity})` |
| `docs/preview/HANDOFF.md` | 主交接文档，铁律 22.2/22.3 记录了透明度问题 |

---

## 八、回滚操作

如果需要回到稳定版本：

```bash
cd "C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild"

# 回滚到最后一个用户确认可用的版本（如果已知）
git checkout <commit-hash>

# 或者回到 a0af3dc（DWM blur-behind 方案，虽然用户不满意但至少 OSD 能显示）
git checkout a0af3dc

# 重新构建
cd desktop-preview
npm run tauri build
```

---

## 九、总结

**本次会话结果**：❌ **失败，OSD 显示功能全部异常**

**根本原因**：
1. 透明度实现涉及多层渲染管线（tao 窗口管理 → DWM 合成 → WebView2 合成 → DOM 渲染），单点修改无法解决全局问题
2. 方案 C（05606fe）的 LAYERED strip 竞态问题确实存在，但 e1aedfd 的色键方案也无法真正工作
3. 误操作删除关键代码（113152e），导致 OSD 基础功能损坏

**建议**：
1. **立即回滚到 a0af3dc 或更早的稳定版本**，恢复 OSD 基础显示功能
2. 在稳定版本基础上，采用**方案 A（WebView2 合成控制）或方案 C（前端自绘）** 进行渐进式修复
3. 每次修改后必须完整测试 OSD 显示 + 透明度滑块 + 拖动模式切换

---

**交接时间**: 2026-09-11 02:30  
**下一步行动**: 回滚到稳定版本，重新评估技术方案
