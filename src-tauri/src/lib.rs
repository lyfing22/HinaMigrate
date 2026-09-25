mod profiles;
mod sidecar;

use sidecar::new_handle;

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(new_handle())
        .invoke_handler(tauri::generate_handler![
            sidecar::sidecar_start,
            sidecar::sidecar_send,
            sidecar::sidecar_stop,
            sidecar::sidecar_running,
            profiles::list_profiles,
            profiles::load_profile,
            profiles::save_profile,
            profiles::delete_profile,
            profiles::get_app_data_dir,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
