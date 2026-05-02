import * as vscode from 'vscode';
import * as path from 'path';
import { CliService } from '../services/cliService';
import { ProjectService } from '../services/projectService';
import { Logger } from '../utils/logger';
import { fileExists } from '../utils/fs';
import { CLI_ENV_PROJECT_ROOT } from '../types';

export function registerImportAssets(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.importAll', async () => {
        try {
            if (!projectService.isLoaded()) {
                vscode.window.showWarningMessage('未加载 UTMT Git 项目，请先初始化项目。');
                return;
            }

            const dataWinPath = projectService.getDataWinPath();
            if (!dataWinPath) {
                vscode.window.showErrorMessage('未配置 data.win 路径。');
                return;
            }

            if (!await fileExists(dataWinPath)) {
                vscode.window.showErrorMessage(`未找到 data.win：${dataWinPath}`);
                return;
            }

            const projectRoot = projectService.getProjectRoot()!;
            const cliPath = projectService.getCliPath();

            const extensionPath = context.extensionPath;
            const resolvedScriptPath = path.join(extensionPath, 'headless-scripts', 'ImportMasterHeadless.csx');

            if (!await fileExists(resolvedScriptPath)) {
                vscode.window.showErrorMessage(
                    `未找到导入脚本：${resolvedScriptPath}。` +
                    '请确保 headless-scripts 目录已包含在扩展中。'
                );
                return;
            }

            logger.show();

            await vscode.window.withProgress(
                {
                    location: vscode.ProgressLocation.Notification,
                    title: '正在导入资产到 data.win...',
                    cancellable: true,
                },
                async (progress, token) => {
                    progress.report({ message: '正在运行 ImportMaster...' });

                    const result = await cliService.importAll(
                        cliPath,
                        dataWinPath,
                        resolvedScriptPath,
                        projectRoot,
                        token
                    );

                    if (result.exitCode === 0) {
                        vscode.window.showInformationMessage('导入完成！');
                        vscode.commands.executeCommand('vscode-utmt-git.refreshTree');
                    } else {
                        vscode.window.showErrorMessage(
                            `导入失败，退出码 ${result.exitCode}。请查看输出面板了解详情。`
                        );
                    }
                }
            );
        } catch (e) {
            logger.error(`导入失败：${e}`);
            vscode.window.showErrorMessage(`导入失败：${e}`);
        }
    });

    context.subscriptions.push(disposable);
}
