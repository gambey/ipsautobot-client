# IpspoolAutomation

## 项目概述

本客户端用于在 **Windows** 上通过 **UI 自动化**（`System.Windows.Automation`）驱动本机已安装的第三方软件，完成兑换、提现等流程中的界面操作。当前内置流程面向「迅捷小辅助」与「迅捷云商家版」两个可执行程序；具体业务步骤在自动化工作流中扩展。

## 被操作软件与路径配置

需要自动化的程序 **exe 路径** 必须在配置中指定，否则无法启动或附着窗口：

| 配置来源 | 说明 |
|----------|------|
| **应用内「设置」** | 可填写或浏览「辅助路径」「商家路径」，保存后写入本地用户目录下的 `ui-settings.json`。 |
| **`appsettings.json`** | 与程序同目录（输出目录中的部署文件），提供默认路径键名：`XunjieHelperPath`、`XunjieMerchantPath`。 |
| **启动时的加载顺序** | 先读取 `appsettings.json`，再若存在 `%LocalAppData%\IpspoolAutomation\ui-settings.json`，则用其中的路径覆盖界面上的路径字段。 |

运行自动化任务时，实际使用的是当前界面上的 **辅助路径** 与 **商家路径**（与保存到 `ui-settings.json` 的内容一致）。执行 `RunAsync`、自动兑换等流程时，均以界面绑定的路径为准（与仅在构造函数中传入配置的初始工作流实例无关）。

## 其他配置

同一 `appsettings.json` 中还包含与服务端通信相关的项，例如：

- `ApiBaseUrl`：API 根地址  
- `ApiTimeoutSeconds`：请求超时（秒）

对接接口时请与项目约定的 API 文档保持一致。

## 前置条件

- **操作系统**：Windows（UI 自动化依赖本机窗口）。
- **运行时**：与项目一致的目标框架，**.NET 8**（Windows）。
- **被操作软件**：已在配置路径下安装并可启动「迅捷小辅助」「迅捷云商家版」（或你替换为同名的可执行文件），否则 `LaunchOrAttach` 无法附着窗口。

## 环境与构建

- **目标框架**：.NET 8（Windows）
- **构建**：在仓库中打开 `IpspoolAutomation` 项目目录后执行 `dotnet build`（或通过 Visual Studio 生成）

## 自动提现（迅捷）

1. 在 **「设置」** 中配置 **辅助路径**（迅捷辅助 exe）、**商家路径**（迅捷云商家版 exe），并填写 **收款人手机号**（对应商家端「给 APP 手机号转账」收款账号），点击 **保存**。
2. 打开左侧 **「提现」** 页面，点击 **执行自动提现**。页面下方文本框会显示运行日志（`LogText`）；运行中可点 **停止** 取消。
3. 流程概要（与 Notion《自动化操作步骤》一致）：
   - 从辅助软件表格读取列 **选择**（数字写入 `RowId`）、**用户名**、**可提收益**（积分），筛选 **可提收益 > 105000**；处理日志中会输出 **rowID** 便于对照表格行；
   - 对每个账号按算法计算 **可提现讯币数量**（见代码 `XunjieAutomationWorkflow.ComputeWithdrawAmountCoins`）；
   - 在辅助软件对该行 **右键 → 显示此号**；**随后再附着迅捷云商家版**（一账号一商家实例时，优先新出现的进程，其次前台为商家 exe 的窗口）；
   - 在商家版 **讯币兑现** 中填写 **兑换讯币数量** 并确定；
   - 在 **会员中心 → 给 APP 手机号转账** 中填写手机号与两档金额并确定；
   - 将 **商家版** 窗口最小化后继续下一账号。

若本机辅助表格的 UI 自动化树与常见 DataGrid/List 不一致，可能需在 `Automation/HelperGridReader.cs` 中按实际控件微调列定位。

## 文档语言约定

本目录内说明文档统一使用**简体中文**。规则见本目录 [`.cursor/rules/documentation-zh.mdc`](.cursor/rules/documentation-zh.mdc)；若以仓库根 `client` 打开整个客户端，另见 [`.cursor/rules/ipspool-automation-docs.mdc`](../.cursor/rules/ipspool-automation-docs.mdc)。
