---
name: git-commit-helper
description: >-
  根据 git diff / staged 变更撰写或润色提交说明，遵循 Conventional Commits 与中文表述习惯。
  在用户请求写 commit message、整理提交说明、根据改动生成提交、或准备 git commit / amend 时使用。
---

# Git 提交助手（git-commit-helper）

## 何时使用

- 用户要求写/补全/润色 **Git 提交说明**、**commit message**、**提交信息**。
- 用户准备 commit、需要概括本次改动、或希望把多条改动拆成多条提交说明。
- 用户提到 **staged**、**暂存**、**diff**、**cherry-pick**、**rebase** 等与提交文案相关的场景。

## 语言与风格

- **默认使用简体中文**撰写标题与正文（与仓库规则一致）。
- 技术专有名词可保留英文：`feat`、`fix`、`refactor`、类名、API、路径、错误原文等。
- 若用户明确要求英文或其它语言，以用户要求为准。

## 工作流（建议顺序）

1. **确认范围**：若用户未说明，先弄清是「仅暂存区」还是「工作区全部」或「指定文件/提交」。
2. **获取事实**：在需要时运行 `git status`、`git diff`、`git diff --cached`（或用户指定的范围），避免臆测未出现的改动。
3. **概括**：用一两句话说明「为什么改」和「改了什么」，避免只列文件名。
4. **输出**：给出可直接粘贴的完整提交说明；若有多条独立逻辑，建议拆成多条 commit 并分别给文案。

## 格式约定

- **推荐** [Conventional Commits](https://www.conventionalcommits.org/)：`type(scope): 简短描述`
- 常用 `type`：`feat`、`fix`、`docs`、`style`、`refactor`、`perf`、`test`、`chore`、`ci`、`build`
- **第一行**（subject）：约 50 字以内、祈使语气、不加句号；必要时带 scope。
- **正文**（可选）：解释动机、边界情况、破坏性变更；与标题空一行。
- **footer**（可选）：如 `BREAKING CHANGE:`、`Refs #123`。

## 示例

**输入（概括）**：修复登录后 Token 未刷新的问题，并补充单元测试。

**输出：**

```text
fix(auth): 登录后正确刷新 Token

避免会话过期仍显示已登录；补充刷新路径的单元测试。
```

**输入（概括）**：重构 MainViewModel，将设置加载抽到独立服务，无行为变更。

**输出：**

```text
refactor(ui): 将客户端设置加载抽离 MainViewModel

提取服务层便于测试与复用；对外行为保持不变。
```

## 与项目规则的关系

若工作区存在 `.cursor/rules` 中关于「提交说明语言」的规则，以该规则为准；本技能侧重 **流程、格式与基于 diff 的准确性**。
