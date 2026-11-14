//! Launcher lifecycle adaptation. All lobby/media/file implementations remain original.
use tauri::Manager;

pub fn enabled() -> bool {
    std::env::var_os("BCCL_MCTIER_EMBEDDED").is_some()
}

#[derive(Clone, serde::Serialize, serde::Deserialize)]
pub struct HostAppearance {
    theme: String,
    background: String,
    top: String,
    bottom: String,
}

#[tauri::command]
pub fn bccl_host_appearance() -> Option<HostAppearance> {
    use std::io::Read;
    if !enabled() { return None; }
    let path = std::env::var_os("BCCL_MCTIER_APPEARANCE_FILE")?;
    let file = std::fs::File::open(path).ok()?;
    let mut bytes = Vec::new();
    file.take(4097).read_to_end(&mut bytes).ok()?;
    if bytes.len() > 4096 { return None; }
    let palette: HostAppearance = serde_json::from_slice(&bytes).ok()?;
    let color = |s: &str| s.len() == 7 && s.starts_with('#') && s.as_bytes()[1..].iter().all(u8::is_ascii_hexdigit);
    if !matches!(palette.theme.as_str(), "light" | "dark")
        || !color(&palette.background) || !color(&palette.top) || !color(&palette.bottom) { return None; }
    Some(palette)
}

pub fn apply_background(window: &tauri::WebviewWindow) {
    if !enabled() || window.label() != "main" { return; }
    if let Some(palette) = bccl_host_appearance() {
        let channel = |start| u8::from_str_radix(&palette.background[start..start + 2], 16).unwrap_or(0);
        let _ = window.set_background_color(Some(tauri::window::Color(channel(1), channel(3), channel(5), 255)));
    }
}

#[cfg(windows)]
fn window_property(window: &tauri::WebviewWindow, name: &str, value: Option<isize>) -> isize {
    #[link(name = "user32")]
    extern "system" {
        fn GetPropW(hwnd: isize, name: *const u16) -> isize;
        fn SetPropW(hwnd: isize, name: *const u16, data: isize) -> i32;
        fn RemovePropW(hwnd: isize, name: *const u16) -> isize;
    }
    let Ok(hwnd) = window.hwnd() else { return 0; };
    let name: Vec<u16> = name.encode_utf16().chain(Some(0)).collect();
    unsafe {
        match value {
            Some(0) => { RemovePropW(hwnd.0 as isize, name.as_ptr()); 0 }
            Some(value) => { SetPropW(hwnd.0 as isize, name.as_ptr(), value); value }
            None => GetPropW(hwnd.0 as isize, name.as_ptr()),
        }
    }
}

pub fn reset_ready(window: &tauri::WebviewWindow) {
    #[cfg(windows)]
    if enabled() && window.label() == "main" { window_property(window, "BCCL_WORKSPACE_READY", Some(0)); }
}

#[tauri::command]
pub fn bccl_workspace_ready(window: tauri::WebviewWindow) {
    if !enabled() || window.label() != "main" { return; }
    apply_background(&window);
    #[cfg(windows)]
    window_property(&window, "BCCL_WORKSPACE_READY", Some(1));
}

#[tauri::command]
pub fn bccl_workspace_active(window: tauri::WebviewWindow) -> bool {
    if !enabled() { return true; }
    #[cfg(windows)]
    { window_property(&window, "BCCL_WORKSPACE_ACTIVE", None) != 0 }
    #[cfg(not(windows))]
    { window.is_visible().unwrap_or(false) }
}

#[cfg(windows)]
pub fn watch_parent(app: &tauri::AppHandle) {
    use std::ffi::c_void;
    #[link(name = "kernel32")]
    extern "system" {
        fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut c_void;
        fn WaitForSingleObject(handle: *mut c_void, timeout: u32) -> u32;
        fn CloseHandle(handle: *mut c_void) -> i32;
    }
    let Some(pid) = std::env::var("BCCL_MCTIER_PARENT_PID").ok().and_then(|p| p.parse::<u32>().ok()) else { return };
    let parent = unsafe { OpenProcess(0x00100000, 0, pid) } as usize;
    if parent == 0 { log::warn!("BCCL parent process is unavailable"); app.exit(1); return; }
    let app = app.clone();
    std::thread::spawn(move || {
        unsafe {
            WaitForSingleObject(parent as *mut c_void, u32::MAX);
            CloseHandle(parent as *mut c_void);
        }
        tauri::async_runtime::spawn(async move {
            // Let the original recorder flush its final MediaRecorder chunk and
            // complete its normal exit command before tearing down capture.
            if crate::modules::screen_recording::finish_before_exit(&app) {
                tokio::time::sleep(std::time::Duration::from_secs(30)).await;
                crate::modules::screen_recording::close_output(); // Preserve recovery bytes if the renderer stalled.
            }
            crate::modules::native_capture::stop_all();
            crate::modules::native_microphone::stop_all();
            if let Some(state) = app.try_state::<crate::AppState>() {
                let core = std::sync::Arc::clone(&state.core);
                let _ = tokio::time::timeout(std::time::Duration::from_secs(20), async {
                    let _ = core.lock().await.shutdown().await;
                }).await;
            }
            app.exit(0);
        });
    });
}
