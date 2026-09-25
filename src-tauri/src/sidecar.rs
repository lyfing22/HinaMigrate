// Sidecar 进程管理：spawn / stdin 写入 / kill，并把 stdout 的行 JSON 转发为 Tauri 事件
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use tauri::async_runtime::{Receiver, spawn as async_spawn};
use tauri::{AppHandle, Emitter, Manager, State};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

/// sidecar 基名（不带目录、不带扩展名）
const SIDECAR_NAME: &str = "fjyxhr-migrate";

/// 应用数据目录下的日志目录 env 名，必须与 C# 端读取的名字一致
const LOG_DIR_ENV: &str = "FJYXHR_LOG_DIR";

/// 全局共享的 sidecar 子进程句柄（None=未运行）。
pub type SidecarHandle = Arc<Mutex<Option<CommandChild>>>;

pub fn new_handle() -> SidecarHandle {
    Arc::new(Mutex::new(None))
}

/// 当前 exe 所在目录（单测位于 target/.../deps 下时回退上一层，与插件内部逻辑一致）。
fn exe_dir() -> Result<PathBuf, String> {
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let dir = exe.parent().unwrap_or(Path::new(".")).to_path_buf();
    if dir.ends_with("deps") {
        if let Some(p) = dir.parent() {
            return Ok(p.to_path_buf());
        }
    }
    Ok(dir)
}

/// 解析 sidecar 相对路径（相对 exe 目录）。
/// `tauri build` 会把 bundle.externalBin 平铺复制到 exe 同目录；打包安装后同样在 exe 同目录。
/// 这里同时兼容放在 `binaries/` 子目录的布局。找不到时返回包含全部候选路径的错误便于诊断。
fn resolve_sidecar_rel() -> Result<PathBuf, String> {
    let base = exe_dir()?;
    let flat = base.join(SIDECAR_NAME).with_extension("exe");
    let nested = base.join("binaries").join(SIDECAR_NAME).with_extension("exe");
    if flat.exists() {
        return Ok(PathBuf::from(SIDECAR_NAME));
    }
    if nested.exists() {
        return Ok(PathBuf::from("binaries").join(SIDECAR_NAME));
    }
    Err(format!(
        "未找到 sidecar 二进制 {}，已尝试:\n- {}\n- {}",
        SIDECAR_NAME,
        flat.display(),
        nested.display()
    ))
}

/// 启动 sidecar（幂等：已在运行则直接返回 Ok）。
/// 注入日志目录 env，spawn 后把 stdout 接收器交给后台 relay 任务逐行转发为 sidecar://event。
///
/// 全程持有互斥锁：若在「检查为 None」和「写入句柄」之间释放锁，
/// 并发调用会各自 spawn 一次，产生不受控的孤儿 sidecar 进程。
pub fn spawn_sidecar(app: &AppHandle, handle: &SidecarHandle) -> Result<(), String> {
    let mut g = handle.lock().map_err(|e| e.to_string())?;
    if g.is_some() {
        return Ok(());
    }

    let log_dir = app
        .path()
        .app_data_dir()
        .map(|d| d.join("logs"))
        .map_err(|e| e.to_string())?;
    std::fs::create_dir_all(&log_dir).map_err(|e| e.to_string())?;
    let log_dir_str = log_dir.to_string_lossy().to_string();

    // 注意：tauri-plugin-shell 的 relative_command_path 会把传入路径整体拼到 exe 目录，
    // 因此这里传入「相对 exe 目录」的路径，而不是 bundle.externalBin 里的项目内路径。
    let rel = resolve_sidecar_rel()?;

    let command = app
        .shell()
        .sidecar(rel)
        .map_err(|e| e.to_string())?
        .env(LOG_DIR_ENV, &log_dir_str);

    let (rx, child) = command.spawn().map_err(|e| e.to_string())?;
    *g = Some(child);

    let handle_clone = handle.clone();
    let app_clone = app.clone();
    async_spawn(async move {
        relay(rx, app_clone, handle_clone).await;
    });

    Ok(())
}

/// 前端命令入口：确保 sidecar 已运行（幂等，重复调用不会报错）。
#[tauri::command]
pub fn sidecar_start(app: AppHandle, handle: State<'_, SidecarHandle>) -> Result<(), String> {
    spawn_sidecar(&app, &handle)
}

/// 向 sidecar stdin 写入一条命令（自动补 \n）。
#[tauri::command]
pub fn sidecar_send(cmd: String, handle: State<'_, SidecarHandle>) -> Result<(), String> {
    let mut g = handle.lock().map_err(|e| e.to_string())?;
    let child = g.as_mut().ok_or("sidecar 未运行".to_string())?;
    let mut data = cmd.into_bytes();
    data.push(b'\n');
    child.write(&data).map_err(|e| e.to_string())?;
    Ok(())
}

/// 停止 sidecar（kill）。relay 任务会在收到 Terminated 后自动清理句柄。
#[tauri::command]
pub fn sidecar_stop(handle: State<'_, SidecarHandle>) -> Result<(), String> {
    let taken = match handle.lock() {
        Ok(mut g) => g.take(),
        Err(e) => return Err(e.to_string()),
    };
    if let Some(child) = taken {
        child.kill().map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// sidecar 是否在运行。
#[tauri::command]
pub fn sidecar_running(handle: State<'_, SidecarHandle>) -> bool {
    handle.lock().map(|g| g.is_some()).unwrap_or(false)
}

/// 后台 relay：逐行接收 sidecar stdout/stderr，按行已是单条 JSON，
/// 解析后 emit 给前端；进程终止时清理句柄。
async fn relay(mut rx: Receiver<CommandEvent>, app: AppHandle, handle: SidecarHandle) {
    while let Some(ev) = rx.recv().await {
        match ev {
            CommandEvent::Stdout(bytes) => {
                let text = String::from_utf8_lossy(&bytes);
                let payload = serde_json::from_str::<serde_json::Value>(&text)
                    .unwrap_or_else(|_| serde_json::json!({ "type": "console", "raw": text }));
                let _ = app.emit("sidecar://event", payload);
            }
            CommandEvent::Stderr(bytes) => {
                let text = String::from_utf8_lossy(&bytes);
                let payload = serde_json::from_str::<serde_json::Value>(&text)
                    .unwrap_or_else(|_| serde_json::json!({ "type": "error", "raw": text }));
                let _ = app.emit("sidecar://event", payload);
            }
            CommandEvent::Error(msg) => {
                let _ = app.emit("sidecar://event", serde_json::json!({ "type": "error", "message": msg }));
            }
            CommandEvent::Terminated(p) => {
                let _ = app.emit(
                    "sidecar://status",
                    serde_json::json!({ "type": "terminated", "code": p.code, "signal": p.signal }),
                );
                if let Ok(mut g) = handle.lock() {
                    *g = None;
                }
            }
            // CommandEvent 标记 #[non_exhaustive]，未来新增变体时兜底不告警
            _ => {}
        }
    }
}
