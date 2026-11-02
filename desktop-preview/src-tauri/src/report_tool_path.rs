use std::path::{Path, PathBuf};

/// The .NET Framework report tools cannot consume Rust's verbatim paths.
/// Ownership checks must happen on the canonical path before this conversion.
pub(super) fn report_tool_path(path: &Path) -> Result<PathBuf, String> {
    let value = path.to_str().ok_or("报告路径包含无法编码的字符")?;
    let normal = if let Some(unc) = value.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{}", unc)
    } else if let Some(disk) = value.strip_prefix(r"\\?\") {
        if disk.as_bytes().get(1) != Some(&b':') {
            return Err("报告工具不支持此设备路径".into());
        }
        disk.to_owned()
    } else { value.to_owned() };
    if normal.encode_utf16().count() >= 260 {
        return Err("报告路径过长，当前报告工具无法处理；请使用较短的数据目录".into());
    }
    Ok(PathBuf::from(normal))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preserves_normal_paths_and_converts_disk_and_unc() {
        for (input, expected) in [
            (r"C:\报告 游戏\run", r"C:\报告 游戏\run"),
            (r"\\?\C:\报告 游戏\run", r"C:\报告 游戏\run"),
            (r"\\?\UNC\nas\reports\run", r"\\nas\reports\run"),
            (r"\\nas\reports\run", r"\\nas\reports\run"),
        ] { assert_eq!(report_tool_path(Path::new(input)).unwrap(), PathBuf::from(expected)); }
    }
    #[test]
    fn rejects_unsupported_devices_and_long_paths() {
        assert!(report_tool_path(Path::new(r"\\?\GLOBALROOT\Device\disk")).is_err());
        assert!(report_tool_path(Path::new(&format!("C:\\{}", "x".repeat(257)))).is_err());
        assert!(report_tool_path(Path::new(&format!("C:\\{}", "x".repeat(256)))).is_ok());
    }
}
