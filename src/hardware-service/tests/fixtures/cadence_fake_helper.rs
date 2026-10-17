use std::io::{BufRead, Write};

fn main() {
    let mut count = 0;
    for line in std::io::stdin().lock().lines() {
        let line = line.unwrap();
        if line.contains("exit") { return; }
        count += 1;
        println!(r#"{{"availability":"available","readCount":{},"dynamic":{{"cpuTemperature":{{"value":60.0}}}}}}"#, count);
        std::io::stdout().flush().unwrap();
    }
}
