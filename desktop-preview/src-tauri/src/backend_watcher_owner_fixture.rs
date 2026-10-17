use std::io::{BufRead, Write};

fn main() {
    println!("ready");
    std::io::stdout().flush().unwrap();
    let mut line = String::new();
    std::io::stdin().lock().read_line(&mut line).unwrap();
}
