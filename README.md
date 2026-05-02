# UTMT Git

将 UndertaleModTool 与 Git 集成，提供游戏资产管理、导入导出和版本控制工具。

## 功能

- **资产管理**：浏览、查看、重命名和删除游戏资产（代码、精灵图、音效、对象、背景、字体）
- **导入导出**：一键从 data.win 导出资产到文件系统，或从文件系统导入回 data.win
- **Git 集成**：资产以文件形式存储，天然支持 Git 版本控制
- **GML 语法高亮**：内置 GML 语法支持和代码片段
- **开箱即用**：内置 UndertaleModCli，无需额外安装依赖

## 使用方法

1. 打开包含 data.win 文件的工作区
2. 运行命令 `UTMT Git: 初始化项目`
3. 在侧边栏的资产浏览器中管理游戏资产

## 扩展设置

- `vscode-utmt-git.cliPath`：UndertaleModCli 可执行文件路径（留空则使用扩展内置的 CLI）
- `vscode-utmt-git.dataWinPath`：默认 data.win 文件路径（相对于项目根目录）
- `vscode-utmt-git.autoExportOnSave`：保存 GML 文件时自动导出资产
- `vscode-utmt-git.autoImportBeforeRun`：执行操作前自动导入所有资产
