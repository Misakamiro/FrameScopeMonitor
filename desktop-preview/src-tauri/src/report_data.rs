use serde_json::Value;

/// Historical data files contain JSON inside one known assignment, never executable input.
pub fn parse(source: &str) -> Result<Value, String> {
    let text = source.trim_start_matches('\u{feff}').trim();
    let json = text
        .strip_prefix("window.FRAMESCOPE_DATA = ")
        .and_then(|s| s.strip_suffix(';'))
        .ok_or("报告数据格式无法识别，请重新生成")?;
    let data: Value = serde_json::from_str(json).map_err(|_| "报告数据损坏，请重新生成")?;
    for key in [
        "target", "run", "notes", "counts", "fps", "system", "process",
    ] {
        if !data.get(key).map(Value::is_object).unwrap_or(false) {
            return Err(format!("报告缺少 {key} 数据，请重新生成"));
        }
    }
    Ok(data)
}

pub fn standalone(template: &str, data: &Value) -> Result<String, String> {
    let json = serde_json::to_string(data)
        .map_err(|e| e.to_string())?
        .replace('<', "\\u003c")
        .replace('\u{2028}', "\\u2028")
        .replace('\u{2029}', "\\u2029");
    let marker = "<script src=\"framescope-interactive-data.js\"></script>";
    if template.matches(marker).count() != 1 {
        return Err("离线报告模板损坏".into());
    }
    Ok(template.replacen(
        marker,
        &format!("<script>window.FRAMESCOPE_DATA = {json};</script>"),
        1,
    ))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn valid() -> String {
        r#"window.FRAMESCOPE_DATA = {"target":{},"run":{},"notes":{},"counts":{},"fps":{},"system":{},"process":{}};"#.into()
    }
    #[test]
    fn accepts_historical_json_wrapper() {
        assert!(parse(&format!("\u{feff}{}\r\n", valid())).is_ok());
    }
    #[test]
    fn refuses_executable_or_missing_data() {
        for s in [
            format!("{}alert(1);", valid()),
            "window.FRAMESCOPE_DATA = {};".into(),
            "window.FRAMESCOPE_DATA = (()=>({}))();".into(),
        ] {
            assert!(parse(&s).is_err());
        }
    }
    #[test]
    fn export_cannot_end_script() {
        let html = standalone(
            "<script src=\"framescope-interactive-data.js\"></script>",
            &serde_json::json!({"name":"</script><script>bad()"}),
        )
        .unwrap();
        assert_eq!(html.matches("</script>").count(), 1);
        assert!(html.contains("\\u003c"));
    }
}
