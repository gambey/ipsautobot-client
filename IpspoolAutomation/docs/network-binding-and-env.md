# 网卡绑定与环境切换说明

## 功能概述

客户端已实现以下能力：

1. 登录成功后自动查询当前账号是否已绑定网卡（MAC）。
2. 未绑定时，自动读取本机网卡列表并上传第一块可用网卡 MAC。
3. 自动化操作（提现、签到）执行前，校验本机网卡列表是否包含服务端绑定 MAC。
4. 不匹配时中止自动化并提示：`本软件不能运行在非绑定平台！`

## 接口对接（以 `api.md` 为准）

- `GET /api/users/mac`：查询绑定网卡。
- `PUT /api/users/mac`：绑定或更新网卡（请求体包含 `mac_addr`）。
- `POST /api/users/mac/verify`：可选的服务端二次校验。

## 登录时绑定流程

1. 登录成功，拿到 JWT。
2. 调用 `GET /api/users/mac` 查询当前账号绑定状态。
3. 若返回 `mac_addr == null`：
   - 枚举本机网卡（过滤回环/隧道，状态为 Up）；
   - 取第一条可用 MAC，规范化为 `AA:BB:CC:DD:EE:FF`；
   - 调用 `PUT /api/users/mac` 绑定。
4. 若 `PUT` 返回 409，视为绑定失败并提示冲突信息（例如 MAC 已被其他账号使用）。

## 自动化前校验流程

在「执行自动提现」和「执行签到」前执行同一前置校验：

1. 调用 `GET /api/users/mac` 获取当前账号绑定 MAC。
2. 读取本机所有可用网卡 MAC（同样做规范化）。
3. 校验本机 MAC 列表是否包含服务端绑定 MAC。
4. 若不包含，提示 `本软件不能运行在非绑定平台！` 并终止流程。

## 本地/线上环境快速切换

配置文件：`appsettings.json`

- `ApiEnvironment`：`local` 或 `prod`
- `ApiBaseUrlLocal`：本地 API 地址
- `ApiBaseUrlProd`：线上 API 地址
- `ApiBaseUrl`：旧配置兼容回退值

推荐配置示例：

```json
{
  "ApiEnvironment": "local",
  "ApiBaseUrlLocal": "http://localhost:3002",
  "ApiBaseUrlProd": "https://www.eiqimaimaia.xyz",
  "ApiBaseUrl": "http://localhost:3002"
}
```

切换方式：

- 切本地：`"ApiEnvironment": "local"`
- 切线上：`"ApiEnvironment": "prod"`

## 连不上本地的快速排查

1. 确认本地服务端进程正在运行，且监听端口与 `ApiBaseUrlLocal` 一致（例如 `3002`）。
2. 在浏览器打开 `http://localhost:3002/api/public-key`，应能返回公钥文本。
3. 确认当前运行目录中的 `appsettings.json` 生效（构建后会复制到 `bin/...`）。
4. 不要在 JSON 中写非法格式；若手动改过，先用标准 JSON 校验。
5. 本机防火墙或安全软件若拦截本地端口，先临时放行再重试。

## 相关代码位置

- `Services/AuthService.cs`：登录后自动绑定逻辑。
- `Services/MacAddressProvider.cs`：本机网卡枚举与 MAC 规范化。
- `Services/NetworkBindingGuard.cs`：自动化前网卡绑定校验。
- `ViewModels/MainViewModel.cs`：提现/签到入口前置校验接入点。
- `Services/AppConfig.cs`：环境切换与 API 地址加载逻辑。
