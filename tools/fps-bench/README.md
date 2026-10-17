# FPS 验收信号源

本工具每帧调用 D3D11 `ClearRenderTargetView` 后 `Present(0, flags)`。
高帧率阶段为缓慢明暗变化的青色，60 FPS 及以下阶段为缓慢明暗变化的红色。
这是低 GPU 负载的提交率信号源，不是游戏负载，也不证明实际显示器上屏帧率或 1% Low。

## 构建和测试

从仓库根目录执行，不会启动 GUI：

```powershell
cargo test --offline --locked --manifest-path tools/fps-bench/Cargo.toml
cargo build --release --offline --locked --manifest-path tools/fps-bench/Cargo.toml
```

不新增依赖。COM vtable 对照本机 Windows SDK 10.0.26100.0：
`IDXGISwapChain::GetBuffer` 槽位 9、
`ID3D11Device::CreateRenderTargetView` 槽位 9、
`ID3D11DeviceContext::ClearRenderTargetView` 槽位 50、
`IDXGIFactory5::CheckFeatureSupport` 槽位 28。
测试中的 COM 替身只验证派发和计数逻辑，不属于真实 D3D 运行证据。

## 操作者运行

以下命令会启动 GUI，只由主 agent 或实机操作者执行。每次只运行一条。

```powershell
$root = 'C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild'
$bench = "$root\tools\fps-bench\target\release\fps-bench.exe"
$logs = "$root\artifacts\20260918-delivery\bench"

& $bench borderless 300 30 --wait-start *> "$logs\borderless-300.log"
& $bench borderless 600 30 --wait-start *> "$logs\borderless-600.log"
& $bench borderless 300 30 --wait-start --steps 300:10,60:10,300:10 *> "$logs\borderless-step.log"
& $bench exclusive 300 30 --wait-start --steps 300:10,60:10,300:10 *> "$logs\exclusive-step.log"
```

`--wait-start` 会无限等待操作者激活窗口并按 Space，不消耗测量时长。
按 Space 后必须连续前台 2 秒；期间失焦会撤销解锁，需要再次按 Space。
设备创建或独占模式切换后，再要求 2 秒稳定前台，然后出现 `measurement_start`。
等待阶段尚未绘制 D3D 内容；无边框窗口没有标题栏，请按进程 PID 定位并激活窗口。
在采样结束前不要切回聊天、终端或其他窗口。Escape 或关闭窗口会取消并判无效。

不带 `--wait-start` 时保留 20 秒前台等待期限，但必须连续前台 2 秒才开始。
`--steps FPS:SECONDS,...` 覆盖固定目标及总时长，阶段顺序和时长由该列表决定。
例如 `600:10,60:10,600:10` 总时长为 30 秒；FPS 为 `0` 表示不主动限速。

## 日志判读

- `waiting` 不属于测量。`measurement_start` 才是有效测量起点。
- `interval` 约每秒输出成功 Present 数、实际区间时长、实际提交率、阶段、
  目标、模式、focus、exclusive、tearing 和 scene；阶段末尾不足一秒标记 `partial=true`。
- 测量中每帧前后验证前台和 DXGI fullscreen 状态，并记录短暂 `WM_KILLFOCUS`。
  失焦、最小化、状态不符、失败或遮挡 Present 均判整次样本无效，不继续伪造成功。
- 只有 `S_OK` 的 Present 会计数，所有中途日志为 `provisional=true`；
  必须结合最后一行及退出码判断整次运行。
- `phase_complete` 是每段平均提交率。任一有上限阶段平均低于目标的 95%，
  最终 `target_met=false`，退出码 `4`。这个阈值只是信号源最低达标检查，
  不是显示器上屏证明，也不是 FrameScope Low 指标的验收结论。
- 退出码 `0`：采样有效、上述最低目标检查通过、清理成功。
  `1`：运行无效或清理失败；`2`：参数错误；`3`：非 Windows；`4`：有效但目标不足。
- `exclusive=true` 表示 `GetFullscreenState` 当时核验一致；
  `scanout_mode=unverified` 始终保留，不能据此推断硬件独占扫描输出或 PresentMon 模式。

支持 tearing 时，窗口/无边框 swapchain 带 `ALLOW_TEARING=0x800`，
`Present` 带 `ALLOW_TEARING=0x200` 且 sync interval 为 0。
独占模式不传 tearing 标志。查询不支持时打印 `LIMITATION`，不静默宣称高帧率已达标。
驱动限帧、DWM、GPU、显示环境仍可能限制速率，以逐秒和逐段实测值为准。

## 本轮验证边界

仅执行自动化测试、格式/静态检查和 Release 编译。
实机 D3D 可见内容、300/600 FPS、失焦操作、阶跃 Low 对照和独占行为：
`NOT_RUN_BY_SCOPE` / `RUNTIME_EVIDENCE_REQUIRED`，由主 agent 激活运行。
本轮未启动本工具 GUI，没有安装、主程序打包、commit 或 push。

## 独立逐帧参考 CSV

可选 `--frame-csv <path>` 记录真实 Present API 调用，不读取或复用主程序
`game_fps` 的帧时、采样窗或 Low 算法。示例由操作者运行，每次只选一条：

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
& $bench borderless 300 40 --wait-start --frame-csv "$logs\$stamp-reference-300.csv" *> "$logs\$stamp-reference-300.log"
& $bench borderless 600 35 --wait-start --frame-csv "$logs\$stamp-reference-600.csv" *> "$logs\$stamp-reference-600.log"
& $bench borderless 300 59 --wait-start --steps 300:12,60:12,300:35 --frame-csv "$logs\$stamp-convergence.csv" *> "$logs\$stamp-convergence.log"
```

路径父目录必须存在，文件必须是新路径；不会覆盖已有采样证据。
启动时预分配并初始化记录内存，采样期间不格式化 CSV、不写磁盘、不扩容。
容量按每阶段 `(2 * target_fps + 1000) * seconds` 加 1024 条余量计算；
不限速阶段按每秒 10000 条预算，最多 400 万条。容量用尽会在下次 Present
之前判无效并导出已有记录，不静默漏帧或扩容。指定的 59 秒阶跃预分配 89664 条。
启用后每帧额外执行两次 QPC 查询和一次内存记录，并非完全零开销；
未指定此参数时不执行额外 QPC 查询，也不创建 CSV 缓冲。

正常结束、失焦、Escape、Present 失败均在停止渲染及清理窗口后统一写 CSV，
最后显式 flush 一次。写入/flush 失败返回 `1`，不会输出最终成功行。
强制结束进程、崩溃或断电无法保证导出；采样期间文件为空是预期行为。

CSV 字段：

| 字段 | 含义 |
| --- | --- |
| `pid`, `frame` | 进程与从 1 开始的真实调用序号 |
| `phase`, `target_fps` | 从 1 开始的阶跃阶段与目标，非实际 FPS |
| `qpc_frequency`, `measurement_start_qpc` | QPC 每秒 ticks 及本次测量起点 |
| `present_start_qpc`, `present_end_qpc` | 在 Clear 完成后、紧邻 Present 调用前后的原始整数 QPC |
| `present_start_seconds`, `present_end_seconds` | 相对测量起点的秒数，仅在退出导出时换算 |
| `hresult`, `present_s_ok` | 原始十六进制 HRESULT，只有精确 `S_OK` 为 true |
| `state_verified` | 本次返回后前台和 fullscreen 核验是否通过 |
| `run_valid` | 整次采样及窗口清理是否有效；失焦等使全部行标记 false |

仅有 `run_valid=true` 且日志含 `frame_csv_complete`、最终完成行、退出码为 `0`
或 `4` 才代表完整有效参考文件；退出码 `4` 仍表示目标速率未达标。
无实际调用时只有表头，不能作为成功样本。CSV 包含失败/遮挡调用，不隐去原始结果。

独立计算时，先选择与 OSD 相同的时间窗口，再用相邻成功且状态有效的
`present_start_qpc` 差除以 `qpc_frequency` 得到提交帧间隔。
`present_end_qpc - present_start_qpc` 只是 API 调用耗时，不能用作 FPS 帧间隔。
第一条记录本身没有前置帧间隔；固定窗口边界是否保留跨界间隔需与 OSD 口径对齐。
不要跨失败调用、无效记录或不同采样文件拼接间隔。工具只提供原始参考，
不计算 1%/0.1% Low，也不证明扫描输出 FPS。
