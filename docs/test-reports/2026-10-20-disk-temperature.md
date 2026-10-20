# 硬盘主温度选取修复

用户提供的 PUBG 记录中，`hardware-samples.csv` 的 519 条 `SsdTempC` 全部为 89.0。原始包不包含各硬盘的完整传感器名称和值，因此不能据此还原真实主温度，也不修改历史数据。

源码原因已确认：`src/hardware-service/src/overlay.rs` 对所有 Storage / Temperature 行直接取最大值，未区分主温度、编号温度、控制器、NAND、热点及阈值。

修复后按硬盘标识分组，优先选择明确命名的 Composite，回退到 Temperature / Drive Temperature，再支持 HDD / SSD Temperature。只比较各盘的主温度；不认识的附属通道不会填入主温度字段。已有瞬时缺失缓存逻辑保持不变，全部传感器列表仍保留原始读数。

验证先失败再通过：新增回归在旧逻辑下分别错误返回 97°C 和 89°C；修复后，混有 89～97°C 附属通道的两块盘选取 43°C、48°C 主温度，最终显示 48°C。只有附属通道或阈值时返回缺失状态。

实际生产 crate 回归命令：

```powershell
cargo test --manifest-path src/hardware-service/Cargo.toml --target-dir C:/fs-build/b201/source/src/hardware-service/target --release --locked --lib overlay::tests -- --test-threads=1
```

结果：9 项通过，0 失败，其余 77 项由测试名称过滤。源码修复未打包、发布或安装；尚未在用户机器上对照主温度实测。

原始包、现场读取与回归日志仅保存在 `artifacts/20261020-overlay-and-disk`，不进入 Git。此提交不宣称修复竖排透明度或 PUBG 全屏可见性。
