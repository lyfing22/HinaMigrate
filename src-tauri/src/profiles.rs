// 配置档 CRUD：每个 profile 以 JSON 文件存于 appDataDir/profiles/<name>.json
use std::fs;
use std::path::PathBuf;

use serde::Serialize;
use tauri::{AppHandle, Manager};

#[derive(Serialize)]
pub struct ProfileMeta {
    pub name: String,
}

fn profiles_dir(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("profiles");
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir)
}

/// 名称只允许字母/数字/下划线/短横线，防路径穿越。
fn valid_name(name: &str) -> Result<String, String> {
    if name.is_empty() {
        return Err("配置档名称不能为空".into());
    }
    if name.len() > 64 {
        return Err("配置档名称过长".into());
    }
    if !name.chars().all(|c| c.is_alphanumeric() || c == '_' || c == '-') {
        return Err("配置档名称仅允许字母、数字、下划线、短横线".into());
    }
    Ok(name.to_string())
}

#[tauri::command]
pub fn list_profiles(app: AppHandle) -> Result<Vec<ProfileMeta>, String> {
    let dir = profiles_dir(&app)?;
    let mut out = Vec::new();
    if let Ok(entries) = fs::read_dir(&dir) {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) == Some("json") {
                if let Some(stem) = path.file_stem().and_then(|s| s.to_str()) {
                    out.push(ProfileMeta { name: stem.to_string() });
                }
            }
        }
    }
    out.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(out)
}

#[tauri::command]
pub fn load_profile(app: AppHandle, name: String) -> Result<serde_json::Value, String> {
    let name = valid_name(&name)?;
    let path = profiles_dir(&app)?.join(format!("{name}.json"));
    let content = fs::read_to_string(&path).map_err(|e| format!("读取失败: {e}"))?;
    serde_json::from_str(&content).map_err(|e| format!("解析失败: {e}"))
}

#[tauri::command]
pub fn save_profile(app: AppHandle, name: String, profile: serde_json::Value) -> Result<(), String> {
    let name = valid_name(&name)?;
    let path = profiles_dir(&app)?.join(format!("{name}.json"));
    let content = serde_json::to_string_pretty(&profile).map_err(|e| e.to_string())?;
    fs::write(&path, content).map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
pub fn delete_profile(app: AppHandle, name: String) -> Result<(), String> {
    let name = valid_name(&name)?;
    let path = profiles_dir(&app)?.join(format!("{name}.json"));
    if path.exists() {
        fs::remove_file(&path).map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
pub fn get_app_data_dir(app: AppHandle) -> Result<String, String> {
    Ok(app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .to_string_lossy()
        .to_string())
}
