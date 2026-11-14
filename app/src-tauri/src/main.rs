#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // A short-lived elevation command has no console window and no PowerShell.
    if let Some(code) = vprint_lib::admin_command_if_requested() {
        std::process::exit(code);
    }
    vprint_lib::run();
}
