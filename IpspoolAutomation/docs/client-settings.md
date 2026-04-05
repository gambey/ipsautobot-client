# 客户端配置（client_settings）：下载、解压、读取与保存

本文说明 IpspoolAutomation 如何从服务端获取 `client_settings.zip`、解压到本地，以及各类 JSON 配置的读取与保存规则。实现以服务端 `api.md` 为准。

## 本地目录

| 用途 | 路径 |
|------|------|
| 主配置目录 | `%LocalAppData%\IpspoolAutomation\client_settings\` |
| 旧版兼容目录 | `%LocalAppData%\IpspoolAutomation\`（仅**读取**时作为回退） |

代码常量：`ClientSettingsPaths.ClientSettingsDirectory`、`ClientSettingsPaths.LegacyAppDirectory`。

## 服务端接口

- **方法 / 路径**：`GET /api/client-settings.zip`
- **鉴权**：`Authorization: Bearer <用户 JWT>`（须为**普通用户**令牌；管理员 JWT 会返回 403）
- **成功响应**：`200`，正文为 ZIP 二进制（`application/zip`）
- **常见错误**：401（未登录/令牌无效或过期）、403（管理员令牌不可用）、404（服务器上未放置 `client_settings.zip`）

服务端从**应用根目录**（与 `package.json` 同级）读取 `client_settings.zip`，而非 `process.cwd()`。详见仓库 `ips_autobot_svr` 的 `api.md` 与 `src/routes/downloads.js`。

## 何时触发下载与解压

在 **`targetSettings.json` 在主配置目录下不存在** 时，会尝试从服务端拉取并解压 ZIP。

触发时机（两处，逻辑相同，第二次为兜底）：

1. **登录成功并完成 MAC 绑定等流程之后**（`AuthService.LoginAsync` 内调用 `EnsureClientSettingsFromServerIfMissingAsync`）。
2. **主窗口首次 Loaded 且已登录**（`App.xaml.cs` 中 `MainWindow_OnFirstLoaded` 再次调用，避免仅冷启动恢复会话时未走登录分支的情况）。

若本地已存在主路径下的 `targetSettings.json`，则**不会**请求 ZIP。

## 客户端下载实现要点

- HTTP 客户端：`ApiClient.DownloadClientSettingsZipAsync`
- 实际请求 URL：在配置的 API 基地址上拼接 `api/client-settings.zip`（与 `api.md` 路径一致）。
- 请求头：携带当前保存的 **Bearer 用户令牌**；`Accept` 包含 `application/zip` 等。

返回结果封装为 `ClientSettingsArchiveResult`：`Success`、`Message`（失败说明）、`Data`（ZIP 字节）。

### 解压行为

- 目标目录：`Path.GetDirectoryName(ICaptureTargetSettingsService.SettingsPath)`，即 **`%LocalAppData%\IpspoolAutomation\client_settings`**（会先 `Directory.CreateDirectory`）。
- 流程：将 ZIP 字节写入**临时文件** → `ZipFile.ExtractToDirectory(..., overwriteFiles: true)` → 删除临时文件。
- 解压或 IO 异常时：**不抛出到上层**（登录仍算成功），用户可稍后手动放置 `targetSettings.json`。

**压缩包布局建议**：ZIP **根目录**直接包含 `targetSettings.json`（以及若需下发的 `dailyCheckExe.json` 等），解压后文件应落在 `client_settings\` 下，避免再多一层 `client_settings\client_settings\` 嵌套。

## 读取（Load）

以下服务均采用同一策略：

- **主路径**：`client_settings\<文件名>`
- **回退路径**：`IpspoolAutomation\<文件名>`（升级前旧位置）
- 使用 `ClientSettingsPaths.ResolveExistingPath(primary, legacy)`：先主后旧；任一路径存在则读取该文件。
- JSON 反序列化使用 `ClientSettingsJson.DeserializeOptions`：**属性名大小写不敏感**、允许注释、允许尾部逗号。

| 文件 | 服务类 | 主路径文件名 |
|------|--------|----------------|
| 捕捉目标设置 | `CaptureTargetSettingsService` | `targetSettings.json` |
| 每日检测可执行配置 | `DailyCheckExeService` | `dailyCheckExe.json` |
| 仅兑换不提现 | `WithdrawOnlySettingsService` | `withdraw_only.json` |
| 仅兑换设置 | `ExchangeScoreSettingsService` | `exchange_score.json` |

读取失败或文件不存在时，各服务通常返回**空默认模型**（如新 `CaptureTargetSettings()`），不阻断启动。

## 保存（Save）

保存时**始终写入主配置目录** `client_settings\` 下对应文件名（不再写回 legacy 目录）：

- 保存前若目录不存在会 `Directory.CreateDirectory`。
- 序列化使用 `ClientSettingsJson.SerializeOptions`（如缩进输出，便于人工编辑）。

因此：用户从旧目录迁移后，一旦在应用内保存，文件会统一到 `client_settings\`。

## 与「仅下载 ZIP」的关系

- ZIP 下载**仅**在缺少 **`targetSettings.json`（主路径）** 时执行；不会因为缺少 `dailyCheckExe.json` 等其它文件而自动拉包。
- 若需在首次部署时一并下发多个 JSON，应在服务器提供的 `client_settings.zip` 中打包多个文件，并保证解压后落在上述 `client_settings` 目录内。

## 相关源码索引

| 说明 | 文件 |
|------|------|
| 路径常量与回退解析 | `Services/ClientSettingsPaths.cs` |
| JSON 选项 | `Services/ClientSettingsJson.cs` |
| ZIP 下载 | `Services/ApiClient.cs` → `DownloadClientSettingsZipAsync` |
| 条件判断 + 解压 | `Services/AuthService.cs` → `EnsureClientSettingsFromServerIfMissingAsync` |
| 启动后兜底调用 | `App.xaml.cs` → `MainWindow_OnFirstLoaded` |
| `targetSettings.json` 读写 | `Services/CaptureTargetSettingsService.cs` |

接口契约以 **`E:\workspace\ipspool\server\ips_autobot_svr\api.md`** 中 `GET /api/client-settings.zip` 章节为准；服务端实现见同仓库 `src/routes/downloads.js`。
