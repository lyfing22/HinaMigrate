# FJYXHR_Migrate_Desk — 福建影像数据迁移工具(桌面版)

在既有控制台迁移程序 `FJYXHR_Migrate_C#` 的基础上，套一层桌面外壳，提供可视化配置、执行、进度、计划与错误管理界面。

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│  fjyxhr-migrate-desk.exe            (Rust / Tauri 宿主)      │
│                                                             │
│   ├─ Vue 3 + TS + Ant Design Vue 前端  ── 内嵌 dist/        │
│   │        │  invoke()                                    │
│   │        ▼                                              │
│   │   invoke_handler: sidecar_* / profiles_*              │
│   │        │                                              │
│   │        ▼                                              │
│   └─ tauri-plugin-shell ── spawn / stdin / kill            │
│           │  行式 JSON  IPC                                │
└───────────┼─────────────────────────────────────────────────┘
            ▼
   fjyxhr-migrate.exe   (C# Sidecar，stdin/stdout JSON 协议)
            │
            ▼
   MongoDB / SQLServer / KingBase  ⇄  达梦 / SQLServer / KingBase
```

三个进程各管一段：**Rust 宿主**只管进程生命周期、stdin/stdout 转发、配置档文件；**C# Sidecar** 承载全部迁移逻辑（与原有控制台程序同源）；**前端**只做 UI 与状态展示。

- 前端**不使用 Pinia**，采用模块级单例 store（`runStore` / `profileStore` / `metaStore`）。
- Sidecar 通过 `stdin` 收请求、`stdout` 发响应与事件，全部为单行 JSON。
- 日志双写：stdout 的 `log` 事件推到 UI，同时滚动落盘到 `%APPDATA%\com.fjyxhr.migrate.desk\logs`。

## 目录结构

```
FJYXHR_Migrate_Desk/
├─ src/                    前端（Vue 3 + TS + Ant Design Vue）
│  ├─ api/                 Tauri invoke 封装（sidecar / profiles / connString）
│  ├─ components/          ConnStringForm / LogPanel / ProgressPanel
│  ├─ layouts/             MainLayout
│  ├─ stores/              run / profile / meta（模块级单例，非 Pinia）
│  ├─ types/               ipc.ts / profile.ts（与 C# DTO 对齐）
│  └─ views/               ConfigPage / RunPage / PlansPage / ErrorsPage / AboutPage
├─ src-dotnet/             C# 侧（.NET 8）
│  ├─ DataMigrate.Engine/      引擎（与源项目同源）
│  └─ DataMigrate.Sidecar/     IPC 外壳：Program.cs 读 stdin 行 JSON 分派
├─ src-tauri/              Rust 宿主
│  ├─ src/lib.rs                 invoke_handler 注册
│  ├─ src/sidecar.rs             spawn / send / stop / relay
│  ├─ src/profiles.rs            配置档 CRUD（%APPDATA%\...\profiles）
│  ├─ binaries/                  Tauri 要求的 sidecar 二进制
│  ├─ capabilities/default.json  ACL 权限
│  └─ tauri.conf.json
├─ scripts/                构建与诊断脚本
└─ dist/                   vite 产物（编译期嵌入宿主）
```

## 前置条件

| 工具 | 版本 | 用途 |
|------|------|------|
| Node.js | 18+ | 前端 |
| pnpm | 9+ | 前端依赖 |
| .NET SDK | 8.0 | 编译 Sidecar |
| Rust + MSVC 工具链 | stable | 编译宿主 |

> 本机已装 **VS 2026 Professional (v18) + Windows SDK 10.0.28000.0**，`vcvarsall.bat`
> 位于 `C:\Program Files\Microsoft Visual Studio\18\Professional\VC\Auxiliary\Build\vcvarsall.bat`，
> Rust 链接器可直接使用，无需额外配置。

## 构建与运行

首次构建需按顺序执行四步（`tauri build` 会自动串起前两步）：

```powershell
pnpm install
pnpm build:sidecar        # ① dotnet publish → src-tauri/binaries/fjyxhr-migrate-<triple>.exe
pnpm build                # ② vue-tsc --noEmit && vite build → dist/
cd src-tauri && cargo build   # ③ Rust 宿主（会 tauri-build 复制 sidecar 到 target/）
pnpm tauri dev            # ④ 开发运行
```

### 发布

**绿色版（推荐，无需任何额外工具）** —— 解压即用，目录里放 2 个文件即可运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-portable.ps1
# -> release-portable/FJYHR_Migrate_Desk/fjyxhr-migrate-desk.exe   11,499,008 B
# -> release-portable/FJYHR_Migrate_Desk/fjyxhr-migrate.exe         44,287,781 B
# -> 合计 53.20 MB
```

脚本内部就是 `pnpm tauri build --no-bundle`（`--no-bundle` 表示跳过打包安装程序这一步），然后把两个 exe 收集到 `release-portable/` 便于分发。已实测该目录拷到别处直接运行正常。

**安装包（需要 WiX / NSIS）**

```powershell
pnpm tauri build       # bundle.targets = ["msi", "nsis"]
```

> 说明：**NSIS / WiX 只用于生成安装程序（setup.exe / msi），与绿色版无关。**
> 绿色版走 `--no-bundle`，完全不经过这两个工具。Windows 上 Tauri 的 `bundle.targets`
> 只有 `msi`/`nsis` 两个可选值（CLI 不接受 `app`/`zip`），想要绿色版就用上面的脚本。

## 关键注意事项（踩坑记录）

1. **Sidecar 路径按 basename 解析。**
   `tauri build` 会把 `bundle.externalBin` 平铺复制到 exe **同目录**（`target/release/fjyxhr-migrate.exe`），
   不带 `binaries/` 前缀。而插件的 `relative_command_path` 会把传入路径**整体**拼到 exe 目录，
   所以代码里必须写 `.sidecar("fjyxhr-migrate")`，写 `"binaries/fjyxhr-migrate"` 会报
   `系统找不到指定的路径 (os error 3)`。`src-tauri/src/sidecar.rs::resolve_sidecar_rel()` 同时兼容
   两种布局，失败时打印全部候选路径。

2. **`plugins.shell` 只接受 `open` 字段。**
   tauri-plugin-shell 2.3.6 的 `Config` 是 `deny_unknown_fields` 且只有 `open`；
   `sidecar` / `scope` 都不是合法字段，写进去会 panic：
   `unknown field 'sidecar', expected 'open'`。因此 `tauri.conf.json` 的 `plugins` 留空 `{}`。

3. **sidecar 白名单写在 capabilities 的 ACL 里。**
   `shell:allow-execute` 本身不带 scope，需用对象形式显式声明：
   ```json
   { "identifier": "shell:allow-execute",
     "allow": [ { "name": "fjyxhr-migrate", "sidecar": true, "args": false } ] }
   ```
   `args: false` 表示不允许任何参数（sidecar 走 stdin/stdout）。
   注意：ACL 只约束 WebView 侧的 `execute` 调用；Rust 侧 `app.shell().sidecar()` 不校验 scope。

4. **环境变量名必须严格一致：`FJYXHR_LOG_DIR`。**
   Rust 端 `.env(LOG_DIR_ENV, ...)` 与 C# 端 `Environment.GetEnvironmentVariable` 的变量名
   只要有一处不一致就静默失效（不会报错），Sidecar 会回退到 exe 目录下的 `logs/`。
   核对方法：看 sidecar 日志首行的 `logDir=`。

5. **`spawn_sidecar` 全程持锁。**
   若「检查句柄为 None」与「写入句柄」之间释放锁，并发调用会各自 spawn 一次，
   产生不受控的孤儿 sidecar 进程（stdout 无人读取，最终阻塞）。
   因此锁跨越整个 spawn 过程，同时保证幂等语义。

6. **`CommandEvent` 是 `#[non_exhaustive]`。**
   `relay` 的 `match` 必须带 `_ => {}` 通配臂，否则 `E0004 non-exhaustive patterns`。

7. **PowerShell 5.1 按 GBK 读取无 BOM 的 UTF-8 脚本。**
   `.ps1` 里硬编码中文路径会被读乱导致路径不匹配。脚本中请用 `$PSScriptRoot` 派生路径，
   不要用中文路径字面量。

8. **NSIS / WiX 只影响安装包，不影响绿色版。**
   绿色版走 `--no-bundle`，不经过任何外部打包工具，本机无需安装 WiX/NSIS 即可产出可分发 exe。
   只有想要 `setup.exe` / `msi` 时才需要装 [WiX v3](https://wixtoolset.org/) 与
   [NSIS](https://nsis.sourceforge.io/)。本机当前未装这两个工具，因此实际发布用的是
   `scripts/build-portable.ps1`。

9. **Git Bash 中不要用裸 `&` 后台化命令链。**
   `( cmd ) &` 只应包住单条命令；若写成 `cd x && ./app > log 2>&1 &`，
   `&` 会把整条 `&&` 链都丢到后台，导致后续命令仍在原目录执行、日志文件找不到。

10. **C# 日志用 Serilog 滚动文件。**
    `WriteTo.File("migrate-.log", rollingInterval: Day)` 实际生成 `migrate-20260923.log`
    （Serilog 把日期补在文件名模式之后）。

## 诊断脚本

| 脚本 | 用途 |
|------|------|
| `scripts/build-sidecar.ps1` | 发布 C# Sidecar 到 `src-tauri/binaries/` |
| `scripts/build-portable.ps1` | 绿色版打包（无需 NSIS/WiX）：`tauri build --no-bundle` 并把两个 exe 收集到 `release-portable/` |
| `scripts/smoke-test.ps1` | 启动宿主，检查宿主与 sidecar 进程是否存活 |
| `scripts/diag.sh` | 同上，并打印 host.log 与 sidecar 日志内容 |
| `scripts/envtest.ps1` | 验证 `FJYXHR_LOG_DIR` 是否被 sidecar 正确读取 |
| `scripts/check-tools.ps1` | 检查 Rust 工具链 / MSVC / SDK |
