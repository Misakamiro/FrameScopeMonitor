// This separately built validation binary has no installer GUI/system adapter.
fn main() {
    if let Err(error) =
        framescope_installer_lib::transaction_validation::run(std::env::args().skip(1).collect())
    {
        eprintln!("Candidate transaction validation failed: {error}");
        std::process::exit(1);
    }
}
