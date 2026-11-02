<div align="center">

<img src="assets/icon/framescope-icon.png" width="112" alt="FrameScope 图标" />

# FrameScope

**把每一帧，放回完整的性能现场。**

Windows 游戏性能监控 · 本地交互报告 · 多形态 OSD · 可定制液态玻璃界面

[![正式版](https://img.shields.io/github/v/release/Misakamiro/FrameScopeMonitor?label=正式版&color=59bfa5)](https://github.com/Misakamiro/FrameScopeMonitor/releases/latest)
[![平台](https://img.shields.io/badge/平台-Windows%2010%20%2F%2011%20x64-4267b2)](#快速上手)
[![源码](https://img.shields.io/badge/公开源码-离线客户端-7c6bc4)](#版型与隐私)
[![许可证](https://img.shields.io/badge/License-GPL--3.0-blue)](LICENSE)

[下载正式版](https://github.com/Misakamiro/FrameScopeMonitor/releases/latest) · [反馈问题](https://github.com/Misakamiro/FrameScopeMonitor/issues) · [源码构建](docs/BUILD.md) · [更新记录](CHANGELOG.md)

</div>

FrameScope 将 FPS、1% Low、0.1% Low、帧时间和进程／硬件采样汇集到一条时间轴。游戏结束后，可以从本地网页报告回看波动、缩放区间、检查原始 CSV；游戏中则用自己喜欢的悬浮窗观察状态。

当前源码基线为 **2.1.2**。本仓库公开离线客户端；Releases 同时承担完整版安装包与对应净化客户端源码附件的分发。安装前请阅读资产名称和发行说明。

## 界面预览

以下图片均于本轮重新生成。主页、硬件页和榜单为 **2.1.2 隔离原生窗口实拍**，使用演示硬件清单和合成榜单；部分实时传感器来自本机。竖排 OSD 为实际原生窗口截图，未运行游戏，FPS / Low 显示 `--`。网页报告由 2.1.2 生成器使用新建合成 CSV 生成。主界面来自完整版，共用本地功能在离线版同样提供；离线版移除了在线入口。

<table>
<tr><td width="50%"><img src="docs/readme/home-mr.webp" alt="深色 MR 主页，演示硬件数据" /><br/><b>深色 MR 主页</b> · 动态光带与玻璃卡片</td><td width="50%"><img src="docs/readme/home-light.webp" alt="浅色透明玻璃主页，原创渐变背景" /><br/><b>浅色透明玻璃</b> · 自定义背景与通透表面</td></tr>
<tr><td><img src="docs/readme/hardware.webp" alt="硬件信息与监控页面，演示数据" /><br/><b>硬件监控</b> · 配置与实时传感器</td><td><img src="docs/readme/report.webp" alt="本地网页报告，合成帧数据" /><br/><b>本地交互报告</b> · 采样筛选与时间轴</td></tr>
<tr><td align="center"><img src="docs/readme/osd-vertical.webp" width="280" alt="2.1.2 竖排 OSD 原生窗口新截图" /><br/><b>竖排 OSD</b> · 原生实窗新截图 · 未运行游戏</td><td><img src="docs/readme/leaderboard-full.webp" alt="完整版榜单预览，合成用户及成绩" /><br/><b>完整版可选榜单</b> · 合成用户与成绩，非生产记录</td></tr>
</table>

[查看配图来源与数据说明](docs/readme/README.md)

## 主要功能

| 功能 | 可以做什么 |
| --- | --- |
| 自动监控目标 | 配置目标进程，等待游戏启动后采集；按设置在会话结束后生成并打开报告。 |
| FPS / Low / 帧时间 | 实时观察最近一秒窗口，报告保留统计版本；查看慢帧和波动，而不只看一个平均数。 |
| 同时间轴诊断 | 关联帧数据、目标进程 CPU／内存／IO 与系统传感器，辅助定位同一时刻发生的变化。 |
| 本地交互报告 | HTML + 数据 JS + manifest 三件套；缩放、拖动、切换曲线和导出图片，保留原始 CSV。 |
| 筛选与重新生成 | 报告顶部的“排除过高/过低 FPS”默认开启；可按报告关闭。有原始输入时可重新生成旧报告。 |
| 多形态 OSD | 横向栏、面板与竖排样式；选择显示项、调整字号与透明度、拖动并保存位置。 |
| 硬件信息 | 查看 CPU、GPU、内存、主板、存储等配置，以及可用的温度、频率、功耗和占用。 |
| 外观定制 | 明暗主题、主题色、字体、导航外观、普通／透明液态玻璃、透明度、模糊、弹跳，以及 MR、图片或视频背景。 |
| 签名更新 | 正式／测试渠道、下载进度、签名与哈希验证、版型校验及安装恢复。 |
| 完整版可选在线功能 | 账号、配置分享、签到、捐助、反馈和成绩榜单；不包含在此仓库的离线实现中。 |

**关于统计：** 统计 v2 的实时 FPS 来自最近一秒有效逐帧数据；Low 对慢帧子集求平均帧时间后换算。报告按秒采样汇总，筛选只影响展示与统计，不改原始 CSV。被排除区间以实线连接保留点，时间轴不压缩，连接段不生成测量值。只有遥测、没有逐帧数据的诊断报告不会生成虚构的 Low。

**2.1.2 外观修复：** 动态背景使用稳定的亮度参考，MR 不随光带逐帧改变字色，视频按来源初始化参考；主动调整外观时重新计算。静态图片仍支持局部背景适配。

## 为什么选择 FrameScope

如果你希望在游戏结束后继续追查“哪一秒发生了什么”，FrameScope 尤其适合：

- **数据可追溯：** 原始帧、进程和系统 CSV 留在本地；报告带输入指纹及统计版本，方便复查和重新生成。
- **结果可解释：** 筛选开关、有效范围和排除数量帮助理解汇总变化；缺失数据明确显示为不可用。
- **实现可阅读：** 离线客户端源码、依赖锁文件和上游说明公开，便于了解采集和报告过程。
- **桌面可按喜好调整：** 普通和透明两种玻璃材质，结合 MR／图片／视频、主题、字体、导航及动画设置。

### 与游戏加加的侧重点对比

游戏加加同样提供 FPS／硬件监控、悬浮显示和皮肤定制。这些是双方共有能力。以下按 [游戏加加官方说明](https://www.gamepp.com/)（2026-11-02 查阅）与本仓库实际功能整理；官网未说明的能力不判定为“不支持”。

| 关注点 | FrameScope | 游戏加加官方介绍 |
| --- | --- | --- |
| 游戏内监控 | FPS / Low、硬件指标与多形态 OSD | FPS、硬件数据、位置及皮肤定制 |
| 游戏结束后的复查 | 本地交互报告、原始 CSV、输入指纹、重新生成与筛选解释 | 强调性能评估及硬件分析；具体导出细节以对应版本为准 |
| 可审阅实现 | 本仓库提供离线客户端源码与统计实现 | 本对比不对其源码开放情况作未经核实的判断 |
| 视觉定制 | 两种液态玻璃、MR、图片／视频、字体、透明度和动画 | 强调监控皮肤及显示自定义 |
| 画面增强与跑分 | 本项目侧重采集、回看与展示 | 官方另介绍 AI 游戏滤镜、电脑跑分等能力 |

FrameScope 的统计 v2 参考普通 FPS / Low 的既定计算口径；不同采集源、帧生成、目标选择和录制区间仍可能造成差异。这不是“更准”“零开销”或性能领先的基准测试。

## 快速上手

1. 前往 [正式版 Releases](https://github.com/Misakamiro/FrameScopeMonitor/releases/latest)，下载 Windows x64 安装包；已有用户可在“设置 → 关于／更新”检查更新。
2. 按提示完成安装及正常的管理员授权。需要读取底层硬件传感器时，依赖安装由用户确认。
3. 在监控目标中启用游戏进程，启动监控，再自行启动游戏。按需要打开并配置 OSD。
4. 游戏结束后进入“报告”，打开本地网页报告；顶部可切换“排除过高/过低 FPS”。
5. 保留原始记录。向他人分享前检查报告中的设备信息、进程名、路径与窗口标题。

### 系统要求与常见问题

| 问题 | 说明 |
| --- | --- |
| 支持什么系统？ | Windows 10 / 11 x64；主界面需要 Microsoft Edge WebView2 Runtime。 |
| 为什么请求管理员权限？ | ETW 采集、硬件服务和部分传感器需要相应权限；请通过正常 UAC 确认。 |
| PawnIO 是什么？ | 部分底层传感器使用的上游驱动。发行包包含原始安装程序，安装需要用户同意。 |
| 为什么显示 `--`、未知或不可用？ | 传感器、驱动、硬件型号和采集权限影响可用性；程序不拿零值代替缺失测量。 |
| 独占全屏能显示 OSD 吗？ | 受游戏呈现模式和系统窗口策略影响；不能保证所有独占全屏场景均能覆盖。 |
| 筛选会修改 CSV 吗？ | 不会。筛选偏好按报告保存；完整版榜单始终采用统一默认筛选，与本地开关独立。 |
| 老报告能重新生成吗？ | 有可用原始输入时可以；缺失原始数据的记录不能恢复丢失的帧。只有 HTML 也不等于完整三件套。 |
| 离线版是否完全禁止网络？ | 本地采集和报告不要求账号；签名更新、依赖恢复及主动启用的公共信息查询可能联网。详见隐私说明。 |

## 版型与隐私

| 分发内容 | 包含范围 |
| --- | --- |
| 本 GitHub 仓库（`EDITION=offline`） | 本地采集、硬件、OSD、报告、设置和签名更新代码；移除完整版在线专属页面、命令和上传实现。 |
| Releases 安装包 | 以发行说明标注版型；完整版仍通过 GitHub 分发和更新。 |
| Releases 的 `full-client-source.zip` | 对应完整版的净化客户端源码；不包含独立服务端、生产部署配置、凭据、密钥或用户数据。 |

本地核心功能不要求登录。完整版的账号与上传功能由用户主动使用；公开源码不等于开放生产服务或服务端。请阅读 [隐私说明](PRIVACY.md)；分享诊断材料前检查并清理个人信息。

## 源码构建与贡献

当前桌面入口为 **Tauri 2 + Rust + React / Chakra UI**，位于 `desktop-preview/`；Rust 硬件服务与 C# 采集／报告工具共同完成本地链路。`src/frontend/` 是保留的兼容前端，不是当前桌面主入口。

- [构建与验证](docs/BUILD.md)：环境、锁定依赖恢复、离线构建和测试。
- [架构与数据契约](docs/ARCHITECTURE.md)：采集、报告三件套、版型边界。
- [贡献指南](CONTRIBUTING.md)：提交范围、复现信息与验证要求。
- [第三方声明](THIRD-PARTY-NOTICES.md) · [GPL-3.0](LICENSE) · [附加声明](desktop-preview/LICENSE.EXTRA)

## 致谢

FrameScope 复用并改编了以下项目的成果，感谢作者与维护者：

| 项目 | 在 FrameScope 中的使用 |
| --- | --- |
| [NexBox](https://github.com/MuLiuSaMa/NexBox) | UI 组件、导航、液态玻璃和动画的复用与改编；MR 背景沿用其实现及对 [Mineradio](https://mineradio.cn/) 的来源致谢。GPL-3.0。 |
| [图吧工具箱 WinUI3 / tubatools](https://github.com/luolangaga/tubatools) | ETW FPS 跟踪、事件优先级及去重等采集实现参考与移植。GPL-3.0。 |
| [PresentMon](https://github.com/GameTechDev/PresentMon) | 图形呈现与帧时间采集。MIT。 |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | 硬件传感器读取。MPL-2.0。 |
| [PawnIO](https://github.com/namazso/PawnIO) / [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) | 底层硬件访问及读取模块；驱动 GPL-2.0，模块 LGPL-2.1。 |
| [Tauri](https://github.com/tauri-apps/tauri) / [React](https://github.com/facebook/react) / [Chakra UI](https://github.com/chakra-ui/chakra-ui) | 桌面宿主与界面基础。各组件许可证见项目及锁定依赖。 |

README 的展示组织参考 NexBox 与 tubatools；配图来自 FrameScope 自身。完整版权、依赖和许可文本见第三方声明，分发时须保留相应声明。
