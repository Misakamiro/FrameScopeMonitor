# 架构与数据契约

当前基线为 2.1.2，公开版型为 `offline`。

## 运行链路

1. `desktop-preview/src/` 的 React / Chakra UI 前端由 Tauri 2 承载；`src-tauri/src/lib.rs` 注册窗口与命令，`backend.rs` 对接本地链路。
2. `src/hardware-service/` 的 Rust 服务负责硬件和实时 FPS；`src/core/frame_statistics.rs` 提供统计核心。
3. `src/app/` 的 C# watcher 与会话 worker 调度 PresentMon、进程和系统采样器，持久化 CSV 与状态。
4. `src/reporting/` 的 C# 生成器读取记录，生成 HTML、数据 JS 和 manifest，通过暂存、校验、发布流程写入。
5. `src-tauri/src/overlay_*` 与独立竖排页面提供 OSD；`updater.rs` 验证签名、哈希与版型。

`src/frontend/` 是兼容前端，构建 C# 链仍需要其产物。多个同名进程可能承担不同采集角色，不能只按名称结束进程。

## 报告

记录保留 `presentmon.csv`、`process-samples.csv`、`system-samples.csv` 及采集状态。`charts/` 下的完整报告由 `framescope-interactive-report.html`、`framescope-interactive-data.js`、`framescope-interactive-manifest.json` 组成。

`full` 表示帧和必需辅助采样可用；`partial` 表示有帧而辅助采样不完整；`diagnostic` 表示只有遥测；`error` 表示数据不足。缺失测量不能补零或沿用上一目标。

统计版本与输入指纹版本不同。重新生成保留输入一致性检查；只有 HTML 不算完整报告。过滤不改原始 CSV，被排除区间的连线不代表新采样。

## 公开边界

本树包含本地功能和签名更新，移除账号、社区、榜单和上传专属实现。完整版源码附件范围不同；服务端与生产配置不属于两者。公开公钥可以提交，私钥不可以。

依赖版本和哈希由锁文件固定。构建、安装、真实硬件和真实游戏分别验收；浏览器演示只证明组件渲染。
