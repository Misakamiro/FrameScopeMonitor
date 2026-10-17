use std::io::{BufRead, Write};

fn main() {
    let path = std::env::var_os("FRAMESCOPE_PIPE_AUDIT_HELPER_LOG").unwrap();
    for line in std::io::stdin().lock().lines() {
        let line = line.unwrap();
        if line.trim() == r#"{"cmd":"exit"}"# {
            std::fs::write(&path, "exit-request\n").unwrap();
            std::thread::sleep(std::time::Duration::from_millis(150));
            std::fs::write(&path, "exit-request\nhelper-exiting\n").unwrap();
            return;
        }
        println!(r#"{{"availability":"available","fixture":"pipe-audit"}}"#);
        std::io::stdout().flush().unwrap();
    }
    std::fs::write(path, "stdin-eof-without-exit-command\n").unwrap();
}
