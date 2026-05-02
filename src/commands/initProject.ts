import * as vscode from 'vscode';
import * as path from 'path';
import { ProjectService } from '../services/projectService';
import { Logger } from '../utils/logger';
import { fileExists } from '../utils/fs';
import { resolveBundledCliPath } from '../utils/cliResolver';

export function registerInitProject(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.initProject', async () => {
        try {
            if (projectService.isLoaded()) {
                const result = await vscode.window.showWarningMessage(
                    '已加载 UTMT Git 项目，是否重新初始化？',
                    '是', '否'
                );
                if (result !== '是') {
                    return;
                }
            }

            const workspaceFolders = vscode.workspace.workspaceFolders;
            let rootDir: string | undefined;

            if (workspaceFolders && workspaceFolders.length > 0) {
                if (workspaceFolders.length === 1) {
                    rootDir = workspaceFolders[0].uri.fsPath;
                } else {
                    const picks = workspaceFolders.map(f => ({
                        label: path.basename(f.uri.fsPath),
                        description: f.uri.fsPath,
                        uri: f.uri,
                    }));
                    const picked = await vscode.window.showQuickPick(picks, {
                        placeHolder: '选择项目所在的工作区文件夹',
                    });
                    if (!picked) {
                        return;
                    }
                    rootDir = picked.uri.fsPath;
                }
            }

            if (!rootDir) {
                const folderUri = await vscode.window.showOpenDialog({
                    canSelectFiles: false,
                    canSelectFolders: true,
                    canSelectMany: false,
                    title: '选择项目根目录',
                });
                if (!folderUri || folderUri.length === 0) {
                    return;
                }
                rootDir = folderUri[0].fsPath;
            }

            const cfg = vscode.workspace.getConfiguration('vscode-utmt-git');
            let dataWinPath = cfg.get<string>('dataWinPath', 'data.win');
            const defaultDataWin = path.join(rootDir, dataWinPath);

            if (!await fileExists(defaultDataWin)) {
                const dataWinUri = await vscode.window.showOpenDialog({
                    canSelectFiles: true,
                    canSelectFolders: false,
                    canSelectMany: false,
                    title: '选择 data.win 文件',
                    filters: {
                        'GameMaker 数据文件': ['win', 'ios', 'droid', 'unx'],
                    },
                    defaultUri: vscode.Uri.file(rootDir),
                });
                if (!dataWinUri || dataWinUri.length === 0) {
                    return;
                }
                dataWinPath = path.relative(rootDir, dataWinUri[0].fsPath);
            }

            const bundledCli = resolveBundledCliPath(context.extensionPath);
            let cliPath = bundledCli || cfg.get<string>('cliPath', 'UndertaleModCli');

            const cliOptions: vscode.InputBoxOptions = {
                prompt: 'UndertaleModCli 可执行文件路径',
                value: cliPath,
                placeHolder: 'UndertaleModCli',
            };
            const inputCliPath = await vscode.window.showInputBox(cliOptions);
            if (inputCliPath !== undefined) {
                cliPath = inputCliPath;
            }

            await vscode.window.withProgress(
                {
                    location: vscode.ProgressLocation.Notification,
                    title: '正在初始化 UTMT Git 项目...',
                    cancellable: false,
                },
                async () => {
                    await projectService.initializeProject(rootDir, dataWinPath, cliPath);
                }
            );

            vscode.commands.executeCommand('setContext', 'vscode-utmt-git.projectLoaded', true);
            vscode.window.showInformationMessage(`UTMT Git 项目已初始化：${rootDir}`);
            vscode.commands.executeCommand('vscode-utmt-git.refreshTree');
        } catch (e) {
            logger.error(`初始化项目失败：${e}`);
            vscode.window.showErrorMessage(`初始化项目失败：${e}`);
        }
    });

    context.subscriptions.push(disposable);
}
