# 通信架构说明

> 范围：只讲**通信与进程协作**，迁移业务逻辑（Engine 内部）不展开。
> 参考规范：[Tauri v2 Sidecar](https://v2.tauri.org.cn/develop/sidecar/)
> 走读文件：`src-tauri/src/{lib,main,sidecar,profiles}.rs`、`src-tauri/tauri.conf.json`、`src-tauri/capabilities/default.json`、`src-tauri/Cargo.toml`、`src-dotnet/DataMigrate.Sidecar/{Program.cs,Ipc/*.cs}`、`src-dotnet/DataMigrate.Engine/Infrastructure/{IpcLogSink,LogHelper}.cs`、`src-dotnet/DataMigrate.Engine/Core/{MigrationOptions,MigrationHost,MigrationRunner}.cs`、`src/api/{sidecar,profiles,connString}.ts`、`src/stores/{run,profile,meta}.ts`、`src/types/{ipc,profile}.ts`、`src/App.vue`、`scripts/build-sidecar.ps1`、`package.json`、`README.md`

---

## 1. 全局视图

三个独立进程，两条官方通道：

```
┌────────────────────────────────────────────────────────────┐
│  fjyxhr-migrate-desk.exe   (Rust / Tauri 宿主进程)          │
│  ┌──────────────────────────┐                              │
│  │ WebView2  (Vue3 + TS)     │                             │
│  │   src/api/sidecar.ts     │                             │
│  │   stores/run.ts          │                             │
│  └───────┬───────────▲──────┘                              │
│          │ invoke()    │ listen('sidecar://event')          │
│          │ 【通道 A】   │ 【通道 B】                          │
│          ▼             │                                   │
│  invoke_handler  ── sidecar.rs / profiles.rs                │
│          │  shell().sidecar().spawn()                       │
└──────────┼─────────────────────────────────────────────────┘
           │ 【通道 C：stdin/stdout 行式 JSON】                │
           ▼                                                  │
  fjyxhr-migrate.exe   (C# .NET 8 Sidecar 进程) ─────────────┘
           │
           ▼
   MongoDB / SQLServer / KingBase
```

四条通道：

| 通道 | 方向 | 载体 | 内容 |
|---|---|---|---|
| **A** | WebView → Rust | `invoke()` (Tauri IPC) | 进程生命周期 + 一行 JSON 命令 + profile CRUD |
| **B** | Rust → WebView | `app.emit()` 事件 | sidecar stdout/stderr 每行 JSON、进程状态 |
| **C** | Rust ↔ C# | 子进程 **stdin / stdout** | 行式 JSON（无长度头、无分隔符协商） |
| **D** | Rust ↔ 磁盘 | 文件 I/O | 配置档 JSON（不经过 C#） |

关键设计：**C# Sidecar 从不需要知道自己是"桌面端"还是"控制台"**——它只面对 stdin/stdout 两条流。桌面外壳与内核通过"契约"而非"依赖"耦合。

---

## 2. 通道 C 详细剖析（最核心，关键节点集中在此）

### 🔑 节点 1：Sidecar 二进制的定位与命名

三处必须严格对齐，否则 `os error 3`（找不到路径）：

```
源码声明:  src-dotnet/DataMigrate.Sidecar.csproj
           <AssemblyName>fjyxhr-migrate</AssemblyName>
                    │
                    ▼
构建脚本:    scripts/build-sidecar.ps1
  dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true
  → src-tauri/binaries/fjyxhr-migrate.exe
  → 重命名为 fjyxhr-migrate-x86_64-pc-windows-msvc.exe   ← target triple 后缀
                    │
                    ▼
打包声明:    src-tauri/tauri.conf.json
  "bundle": { "externalBin": ["binaries/fjyxhr-migrate"] }
                    │
                    ▼  tauri build 时"平铺"复制到 exe 同目录
产出布局:    target/release/fjyxhr-migrate-desk.exe
            target/release/fjyxhr-migrate.exe              ← 已无 binaries/ 前缀
```

**为什么必须手写解析**（`src-tauri/src/sidecar.rs::resolve_sidecar_rel`）：

插件内部的 `relative_command_path` 会把传入路径**整体**拼到 exe 目录。所以：

- 打包后：exe 同目录平铺 → 传 `"fjyxhr-migrate"`
- 源码布局：`binaries/fjyxhr-migrate-<triple>.exe` → 传 `"binaries/fjyxhr-migrate"`

代码两种布局都探测，找不到时把**全部候选路径**塞进错误信息，避免黑盒排障：

```rust
fn resolve_sidecar_rel() -> Result<PathBuf, String> {
    let flat   = base.join(SIDECAR_NAME).with_extension("exe");
    let nested = base.join("binaries").join(SIDECAR_NAME).with_extension("exe");
    ...
    Err(format!("未找到 sidecar 二进制 {}，已尝试:\n- {}\n- {}", ...))
}
```

> **注意**：`tauri build` 会消费 `-<triple>` 后缀的文件名，但**运行时**传入的相对路径**不带后缀**。这是 Tauri 约定，最容易写错。

### 🔑 节点 2：ACL 白名单（`src-tauri/capabilities/default.json`）

```json
{
  "identifier": "shell:allow-execute",
  "allow": [
    { "name": "fjyxhr-migrate", "sidecar": true, "args": false }
  ]
}
```

三个开关的含义：

| 字段 | 作用 |
|---|---|
| `sidecar: true` | 声明这是 sidecar（宿主自带、随包分发），区别于任意外部命令 |
| `args: false` | 禁止任何命令行参数 —— 本项目的参数全部走 stdin，不需要 argv |
| `name` | 与 `resolve_sidecar_rel` 返回值严格一致 |

**重要不对称**：ACL 只约束 **WebView 侧** `Command.sidecar()` 调用；**Rust 侧 `app.shell().sidecar()` 不校验 scope**。因此本项目全部用 Rust 侧发起，ACL 实际是"防御性声明"，真正的隔离靠 `shell().sidecar()` 而非 `shell().execute()`（后者才是任意进程执行）。

另外 `plugins.shell` 在 Tauri 2.3.6 是 `deny_unknown_fields` 且只认 `open` 字段，`sidecar` / `scope` 写进去会 panic，所以 `tauri.conf.json` 的 `"plugins": {}` 必须留空。

### 🔑 节点 3：进程句柄与 spawn 的锁语义

```rust
pub type SidecarHandle = Arc<Mutex<Option<CommandChild>>>;

pub fn spawn_sidecar(app: &AppHandle, handle: &SidecarHandle) -> Result<(), String> {
    let mut g = handle.lock()?;
    if g.is_some() { return Ok(()); }   // 幂等
    ...
    let (rx, child) = command.spawn()?;
    *g = Some(child);                   // ← 锁不释放，避免并发双 spawn
    async_spawn(relay(rx, ...));        // relay 是独立任务，不占锁
}
```

**为什么锁要跨整个 spawn**：若在"检查 None"和"写入 Some"之间释放锁，两个并发 `invoke('sidecar_start')` 会各自 spawn 一次 → 产生孤儿 sidecar，其 stdout 无人读取 → 管道写满 → C# 端 `_out.WriteLine` 永久阻塞。这是典型的"检查-执行"竞态。

代价：`spawn` 期间阻塞其他命令的 `sidecar_send`。可接受——启动只发生一次。

### 🔑 节点 4：Relay 泵（`relay` async fn）

`tauri-plugin-shell` 的 `spawn()` 返回 `(Receiver<CommandEvent>, CommandChild)`。`rx` 是**事件队列**，不是原始字节流——插件内部已经把子进程的 stdout/stderr/退出封装成强类型枚举：

```rust
while let Some(ev) = rx.recv().await {
    match ev {
        CommandEvent::Stdout(bytes) => emit("sidecar://event", parse_json_or_fallback(bytes)),
        CommandEvent::Stderr(bytes) => emit("sidecar://event", parse_json_or_fallback(bytes)),
        CommandEvent::Error(msg)    => emit("sidecar://event", {"type":"error", "message": msg}),
        CommandEvent::Terminated(p) => { emit("sidecar://status", {"type":"terminated", code, signal}); clear_handle(); }
        _ => {}   // CommandEvent 是 #[non_exhaustive]，必须兜底
    }
}
```

**关键设计**：Rust 端**不做行缓冲**。它信任 C# 端保证"每行一个完整 JSON"，收到多少字节就尝试解析多少：

```rust
let payload = serde_json::from_str::<Value>(&text)
    .unwrap_or_else(|_| json!({ "type": "console", "raw": text }));
```

解析失败不丢弃，降级为 `console` 事件带 `raw` 字段。**这是一个优雅的容错**——即使 C# 端某处漏改了输出格式，前端仍能展示原文，而不是静默丢失。

> ⚠️ 隐含契约：如果 C# 端某次真的输出了**多行**（未 flush 到行边界），插件会把整块作为一个 `Stdout` 事件送来，JSON 解析会失败 → 全部降级成一条 `console`。所以 C# 端必须严格 flush（见节点 6）。

### 🔑 节点 5：stdin 写入与请求关联

```rust
pub fn sidecar_send(cmd: String, handle: State<'_, SidecarHandle>) -> Result<(), String> {
    let mut data = cmd.into_bytes();
    data.push(b'\n');            // ← Rust 端补行终止符
    child.write(&data)
}
```

协议极简：**没有长度前缀、没有消息边界协商、没有心跳**。就是 `\n` 分隔的单行 JSON。可靠性靠三层：

1. C# 端 `ReadLineAsync()` 按行读，天然对齐
2. Rust 端写入前补 `\n`，前端不关心
3. 前端 `pending` Map + **30s 兜底超时**，防永久挂起

### 🔑 节点 6：C# 端 stdout 契约（"stdout 永远合法 JSON 流"）

这是全项目最精巧的三件套。核心不变量：

> **`fjyxhr-migrate.exe` 的 stdout 上，每一个字符序列以 `\n` 结尾的部分，都是一条合法 JSON。**

为此需要拦截 C# 程序里**所有**可能污染 stdout 的来源。共三个来源、三种拦截手段：

| 来源 | 拦截器 | 机制 |
|---|---|---|
| 显式 IPC 输出 | `IpcWriter.Emit` | 序列化 + `lock` + `WriteLine` + `Flush` |
| 残留的 `Console.WriteLine`（如 `MigrationStatistics.PrintSummary` 的 ASCII 汇总表） | `ConsoleRedirector` | `Console.SetOut()` 重定向，按行缓冲后包装成 `{"type":"console"}` |
| Serilog 日志 | `IpcLogSink` | 自定义 `ILogEventSink`，渲染成 `{"type":"log",...}` |

**`Program.cs` 的启动顺序是关键**（顺序错了就全崩）：

```csharp
// 1. 先保存真实 stdout/stderr 引用
var realStdout = Console.Out;
var realStderr = Console.Error;

// 2. 用真实句柄创建 IpcWriter
var ipc = new IpcWriter(realStdout, realStderr);

// 3. 把 Console.Out 重定向为 ConsoleRedirector（内部调 ipc）
Console.SetOut(new ConsoleRedirector(ipc));

// 4. Serilog 挂 IpcLogSink（也调 ipc）
var logger = LogHelper.CreateIpcLogger(ipc.EmitRaw, logDir);
```

第 3 步之后，即使 Engine 里还有几十处 `Console.WriteLine`（确实有，见 `MigrationStatistics.PrintSummary`），也不会污染 JSON 流。这是**向后兼容遗留控制台代码**的关键。

`IpcWriter` 的线程安全设计值得注意——Sidecar 有 Timer（500ms 进度上报）、后台 Task（迁移）、Serilog 后台线程，**多写者**：

```csharp
private readonly object _lock = new();
public void Emit(object payload) {
    var json = JsonSerializer.Serialize(payload, EmitOpts);  // ← 锁外序列化（避免持锁做慢操作）
    lock (_lock) { _out.WriteLine(json); _out.Flush(); }
}
```

`EmitError` 同时写 stdout **和** stderr：stdout 走正常事件流，stderr 让 Rust 端 `CommandEvent::Stderr` 分支也能捕获（前端会重复收到，但换来了"绝不丢失"）。

### 🔑 节点 7：stdin 命令循环与分派

```csharp
while ((line = await stdin.ReadLineAsync()) is not null) {
    if (string.IsNullOrWhiteSpace(line)) continue;
    var req = JsonSerializer.Deserialize<IpcRequest>(line, ParseOpts);  // CaseInsensitive
    if (req is null || string.IsNullOrWhiteSpace(req.Cmd)) { ipc.EmitError(...); continue; }
    await host.HandleAsync(req);
}
```

- 单线程顺序处理请求（`await` 分派器），**但长任务立即分叉**（见节点 8）
- 解析失败**不退出循环**，发一条 error 事件继续读下一行——单条脏数据不会打死整个 sidecar
- stdin EOF（Rust 端 `child.kill()` 或管道关闭）→ 循环退出 → `Dispose()` → 进程干净退出

请求格式：

```json
{ "id": "r7", "cmd": "start", "args": { "profile": { ... } } }
```

### 🔑 节点 8：短任务 vs 长任务的分流

`SidecarHost.HandleAsync` 的 switch 把命令分成两类，**这是整个异步模型的核心**：

**短任务（同步 Respond）**：`ping` / `initDb` / `getPlans` / `getErrors` / `supportedTypes`

```csharp
Respond(req, new { id = req.Id, type = "result", ok = true, data });
```

直接 `await` 完成后发 `result`，一条请求一条响应。

**长任务（立即 `started` + 后台 Task + 后续流式）**：`start` / `retryErrors`

```csharp
_ipc.Emit(new { id = req.Id, type = "started", label, mode });   // ← 立即回
_ = Task.Run(async () => {
    // ① 构建 DI
    // ② 注入 IpcProgressReporter（Timer 每 500ms 发 progress）
    // ③ 执行迁移
    // ④ 发 result（最终）
});
```

配合 `stop`（发 `CancellationToken`，**不 kill 进程**——C# 端优雅取消，Rust 端 kill 只用于进程级故障）。

**并发保护**：`_running` 标志 + `_runCts`。第二次 `start` 会收到 `"已有任务运行中，请先停止"`，而不是产生第二个后台 Task。

**进度上报器** `IpcProgressReporter`：实现 `IProgressReporter`，通过 DI 注入 `MigrationRunner`。500ms 周期 Timer 读 `MigrationStatistics` 快照 → `ipc.Emit({type:"progress",...})`。这是**推模式**，前端完全不需要轮询。

### 🔑 节点 9：前端 request/response 关联（`src/api/sidecar.ts`）

Tauri 的 `invoke()` 是 Promise-based，但**通道 B 是纯推送**——如何把某个 `result` 事件匹配回发起它的 `invoke`？用 `id` + `pending` Map：

```ts
let seq = 0
const pending = new Map<string, { resolve, reject }>()

export async function sidecarSend(cmd, args = {}) {
  const id = `r${++seq}`
  await invoke('sidecar_send', { cmd: JSON.stringify({ id, cmd, args }) })
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    setTimeout(() => {                        // 30s 兜底
      if (pending.has(id)) { pending.delete(id); reject(new Error('命令超时未响应')) }
    }, 30_000)
  })
}
```

**精妙之处**：对长任务，监听器收到 `started` 就**提前 resolve**：

```ts
if (id && pending.has(id)) {
  if (ev.type === 'started') {
    p.resolve(ev)                              // ← Promise 完成，cmdStart 返回
  } else if (ev.type === 'result') {
    ev.ok ? p.resolve(ev) : p.reject(...)
  } else {
    h.onEvent(ev)                              // progress/log 走事件流，不进 Promise
  }
}
```

这样 `await cmdStart(profile)` 立即返回（不等迁移完成），而 `progress` / `result` 由 `runStore.dispatch()` 独立消费。

**`runStore` 是单例 store**（不用 Pinia，模块级 class 实例），`ensureListening()` 幂等注册监听。日志用 `shallowRef` + 5000 条环形上限（超出切片 `slice(-5000)`），避免高频 progress/log 撑爆内存。

### 🔑 节点 10：数据契约双向镜像

`MigrationOptions` 存在**两份**，必须手工人肉对齐：

```
C#:  DataMigrate.Engine/Core/MigrationOptions.cs        (PascalCase 属性)
TS: src/types/profile.ts                                 (camelCase interface)
```

**为何能工作**：C# 端反序列化用 `PropertyNameCaseInsensitive = true`，JSON 序列化用 `JsonNamingPolicy.CamelCase`。所以只要字段名对齐即可，大小写不敏感。

传输链路：

```
TS MigrationOptions ──JSON──► Rust (透传字符串, 不解析) ──stdin──► C# JsonElement
                                                                        │
                                                             GetProfile()
                                                             TryGetProperty("profile")
                                                                        ▼
                                                          Deserialize<MigrationOptions>()
```

注意 Rust 端 `sidecar_send` **只接收 `String`，完全不解析 JSON**——宿主是纯管道，不承载任何业务语义。这让 Rust 层保持极薄，也意味着**契约校验完全在 C# 端**（`GetProfile()` 抛异常 → `EmitError`）。

### 🔑 节点 11：配置档走独立通道（不经 C#）

`profiles.rs` 全部是纯 Rust 文件系统操作：

```
%APPDATA%\com.fjyxhr.migrate.desk\profiles\<name>.json
%APPDATA%\com.fjyxhr.migrate.desk\logs\migrate-<date>.log
```

路径穿越防护（`valid_name`）：只允许 `[A-Za-z0-9_-]`，长度 ≤ 64。这是**唯一**的注入面（配置档名 → 文件名），值得单独防御。

C# 端的日志目录通过 **环境变量注入**，不是命令行参数（因 `args: false`）：

```rust
.env(LOG_DIR_ENV, &log_dir_str)     // LOG_DIR_ENV = "FJYHDR_LOG_DIR"
```
```csharp
var logDir = Environment.GetEnvironmentVariable("FJYHDR_LOG_DIR") ?? ...
```

> **踩坑**：变量名两端必须逐字符一致，不一致**不会报错**，静默回退到 exe 目录。这是无测试覆盖的经典故障。

---

## 3. 端到端时序

### 启动

```
WebView App.vue onMounted
  │
  ├─► invoke('sidecar_start')                          ──► Rust spawn_sidecar
  │                                                        │ 解析路径 / 建 logs 目录
  │                                                        │ shell().sidecar().spawn()
  │                                                        │ 注入 FJYHDR_LOG_DIR
  │                                                        │ *handle = Some(child)
  │                                                        │ async_spawn(relay)
  │      ◄─────────────── Ok ──────────────────────────────┘
  │
  └─► C# Program.cs 启动:
        捕获 stdout → IpcWriter → Console.SetOut(ConsoleRedirector)
        → LogHelper.CreateIpcLogger(IpcLogSink)
        → logger.Information("Sidecar 启动")
              │
              └─► stdout: {"type":"log","level":"INF",...}
                    │
                    ▼ (relay 泵)
              Rust emit("sidecar://event", payload)
                    │
                    ▼
              WebView listen 回调 → runStore.dispatch → pushLog
```

### 短命令（如"测试连接"）

```
用户点"测试" → cmdPing('source', connStr)
  │  id = "r3"
  ├─► invoke('sidecar_send', { cmd: '{"id":"r3","cmd":"ping","args":{...}}' })
  │        → Rust child.write(bytes + '\n')
  │                → C# ReadLineAsync → HandleAsync
  │                → DataAccessorFactory.Create → PingAsync
  │                → _ipc.Emit({id:"r3", type:"result", ok:true, data:{...}})
  │                        → stdout → relay → emit("sidecar://event")
  │                                → pending.get("r3").resolve(ev)
  │                                        → Promise resolve → UI 更新
```

### 长命令（批量迁移）

```
用户点"开始" → runStore.start(profile) → cmdStart(profile)
  │  id = "r5"
  ├─► ... → C# RunBackground
  │         _ipc.Emit({id:"r5", type:"started", ...})     ←── ① resolve Promise
  │         _ = Task.Run(...)                              ←── ② 分叉，HandleAsync 返回
  │
  │   [500ms Timer] ─► Emit({type:"progress", total, processed, rps})   流式
  │   [Serilog]     ─► Emit({type:"log", ...})                        流式
  │   [完成]        ─► Emit({id:"r5", type:"result", ok, summary})    终结
  │
  └─► runStore.dispatch 按 type 分流: progress→进度条, log→日志, result→状态=done
```

### 停止

```
用户点"停止" → runStore.stop() → invoke('sidecar_stop')
  ├─► Rust: child.kill()            ← 杀 C# 进程（硬停）
  └─► C# 侧优雅取消：invoke('sidecar_send', { cmd: '{"cmd":"stop"}' })
                          → SidecarHost.StopRun → _runCts.Cancel()
                          → OperationCanceledException → Emit(result, ok=false, summary="已取消")
```

> ⚠️ 注意这里存在**两套停止语义**：前端 `runStore.stop()` 调的是 Rust `sidecar_stop`（kill 进程），而 `cmdStop` 语义（发 `stop` 命令给 C#）在代码中并未暴露到前端 store。当前实现下"停止"会直接杀掉整个 sidecar，副作用是重启才能继续（宿主 `spawn_sidecar` 幂等，会重新拉一个）。这是可优化点（见第 6 节）。

---

## 4. 事件类型总表（通道 B 载荷契约）

| `type` | 触发方 | 用途 | 关联 id |
|---|---|---|---|
| `log` | Serilog via `IpcLogSink` | 结构化日志（DBG/INF/WRN/ERR/FTL） | 无 |
| `console` | `ConsoleRedirector` | 残留 `Console.WriteLine`（含 ASCII 汇总表） | 无 |
| `progress` | `IpcProgressReporter` Timer | 迁移进度（500ms 周期） | 无 |
| `started` | `RunBackground` | 长任务已受理 | ✅ **resolve Promise** |
| `result` | 所有命令 | 最终结果（短任务或长任务终结） | ✅ **resolve/reject Promise** |
| `error` | 异常捕获 / 解析失败 | 错误 | 有（尽力关联） |
| `debug` | `RunDebugAsync` | 调试模式回传单条数据 | 无 |
| `terminated` | Rust relay（`CommandEvent::Terminated`） | 走 `sidecar://status`，非 event | 无 |

**前端类型定义**：`src/types/ipc.ts`，与 C# 输出严格镜像。改一端必须改另一端——**没有 schema 校验层**，是当前的主要技术债。

---

## 5. 关键设计决策小结

| 决策 | 理由 | 代价 |
|---|---|---|
| Sidecar 走 stdin/stdout 而非 socket/pipe | Tauri 官方支持路径最短；无端口分配；无防火墙问题 | 无法双向并发（stdin 只能写、stdout 只能读，但足够） |
| 行式 JSON，无长度前缀 | 简单；C# `ReadLineAsync` 原生支持 | 依赖"每行一条"的强契约；多行输出会降级 |
| 宿主 `sidecar_send` 只收 String | Rust 层零业务耦合，纯管道 | 契约校验全在 C#；前端类型错误要到运行时才暴露 |
| stdout 三件套拦截 | 让遗留控制台代码（Console.WriteLine）无侵入运行 | 增加一层复杂度；`ConsoleRedirector` 需处理 `Write(char)` 逐字符 |
| Serilog 自定义 Sink 而非文件+tail | 单通道、无竞态、天然 JSON | 日志与 IPC 共命运（sidecar 挂 = 日志停） |
| 锁跨整个 spawn | 避免孤儿进程 | 启动期间阻塞其他命令 |
| `started` 提前 resolve | 前端不阻塞 | 需要区分"任务已受理"与"任务完成"两个状态 |
| 30s 兜底超时 | 防永久挂起 | 长任务不受影响（已 resolve），短命令 30s 足够 |
| 配置档在 Rust 侧处理 | 不污染 sidecar；离线可用 | profile 数据不经 C# 校验 |

---

## 6. 局限与改进建议

1. **缺少 `cmdStop` 前端封装**：`stop` 命令（优雅取消）已实现但前端未暴露，当前"停止"= kill 进程。应加 `export const cmdStop = () => sidecarSend('stop')`，`runStore.stop()` 优先发命令、超时后再 kill。

2. **无 schema 校验**：`types/ipc.ts` 与 C# DTO 靠人肉对齐。建议引入 `json-schema-to-typescript` 从 C# 生成，或用 zod/valibot 在 `dispatch` 入口做一次运行时校验（解析失败目前会静默走 `default` 分支）。

3. **日志无去重**：`EmitError` 同时写 stdout+stderr，前端会收到两次。可在 `dispatch` 按 `message+ts` 去重，或改为只在 stdout 发 error、stderr 仅保留给 Rust 侧诊断。

4. **`pending` Map 无清理保证**：若 sidecar 崩溃，未完成的 Promise 依赖 30s 超时。可在 `sidecar://status` 的 `terminated` 回调里统一 reject 所有 pending。

5. **环境变量名无编译期校验**：`FJYHDR_LOG_DIR` 两端字符串字面量，建议抽成共享常量文件（如从 C# 生成 `.env.keys.json`，Rust build.rs 读入）。

6. **单 sidecar 单任务**：`_running` 标志限制同时只能一个迁移。若未来要"多计划并行"，需把 `_runCts` 改为 `Dictionary<string, CancellationTokenSource>`，`id` 关联任务。当前架构下前端 `pending` Map 已支持多任务，只有 C# 侧是瓶颈。

7. **`CommandEvent::Stderr` 事件目前也 emit 到 `sidecar://event`**：与 stdout 混流。可考虑单独 `sidecar://stderr` 事件，让前端能区分"业务错误"与"进程崩溃前的原生 stderr"。

8. **绿色版依赖"两个 exe 同目录"**：`resolve_sidecar_rel` 兼容 `binaries/` 子目录布局但实际发布不用。若未来要"宿主与 sidecar 分离部署"，可考虑支持从配置文件读 sidecar 路径。

---

## 7. 一句话总结

> **一个 Rust 宿主做"薄管道"，把 WebView 的 `invoke` 降级成子进程的 stdin 行 JSON，再用事件队列把子进程 stdout 升级回 Tauri 事件；C# 内核通过三件套拦截器保证"stdout 每行都是合法 JSON"这一唯一不变量，从而让遗留控制台代码无改造迁入桌面外壳。**

核心风险点集中在**通道 C 的隐式契约**（行式 JSON、`FJYHDR_LOG_DIR` 字符串一致、字段名 camelCase 对齐、spawn 锁语义），这些都没有编译期保障，靠 README 的 10 条踩坑记录 + 代码注释人工维护。
