import * as vscode from 'vscode';
import * as path from 'path';
import { CliService } from '../services/cliService';
import { ProjectService } from '../services/projectService';
import { Logger } from '../utils/logger';
import { fileExists } from '../utils/fs';
import { AssetType, CLI_ENV_PROJECT_ROOT } from '../types';

const ASSET_TYPE_NAMES: Record<AssetType, string> = {
    code: '代码',
    sprites: '精灵图',
    sounds: '音效',
    objects: '对象',
    backgrounds: '背景',
    fonts: '字体',
    rooms: '房间',
};

export function registerExportAssets(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    registerExportCode(context, projectService, cliService, logger);
    registerExportSprites(context, projectService, cliService, logger);
    registerExportSounds(context, projectService, cliService, logger);
    registerExportObjects(context, projectService, cliService, logger);
    registerExportBackgrounds(context, projectService, cliService, logger);
    registerExportFonts(context, projectService, cliService, logger);
    registerExportRooms(context, projectService, cliService, logger);
    registerExportAll(context, projectService, cliService, logger);
}

function registerExportCode(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportCode', async () => {
        await exportAssetType(projectService, cliService, logger, 'code');
    });
    context.subscriptions.push(disposable);
}

function registerExportSprites(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportSprites', async () => {
        await exportAssetType(projectService, cliService, logger, 'sprites');
    });
    context.subscriptions.push(disposable);
}

function registerExportSounds(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportSounds', async () => {
        await exportAssetType(projectService, cliService, logger, 'sounds');
    });
    context.subscriptions.push(disposable);
}

function registerExportObjects(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportObjects', async () => {
        await exportAssetType(projectService, cliService, logger, 'objects');
    });
    context.subscriptions.push(disposable);
}

function registerExportBackgrounds(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportBackgrounds', async () => {
        await exportAssetType(projectService, cliService, logger, 'backgrounds');
    });
    context.subscriptions.push(disposable);
}

function registerExportFonts(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportFonts', async () => {
        await exportAssetType(projectService, cliService, logger, 'fonts');
    });
    context.subscriptions.push(disposable);
}

function registerExportRooms(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportRooms', async () => {
        await exportAssetType(projectService, cliService, logger, 'rooms');
    });
    context.subscriptions.push(disposable);
}

function registerExportAll(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.exportAll', async () => {
        try {
            if (!projectService.isLoaded()) {
                vscode.window.showWarningMessage('未加载 UTMT Git 项目。');
                return;
            }

            const types: AssetType[] = ['code', 'sprites', 'sounds', 'objects', 'backgrounds', 'fonts', 'rooms'];

            await vscode.window.withProgress(
                {
                    location: vscode.ProgressLocation.Notification,
                    title: '正在导出所有资产...',
                    cancellable: true,
                },
                async (progress, token) => {
                    for (let i = 0; i < types.length; i++) {
                        if (token.isCancellationRequested) {
                            break;
                        }
                        progress.report({
                            message: `正在导出${ASSET_TYPE_NAMES[types[i]]}...`,
                            increment: (100 / types.length),
                        });
                        await exportAssetTypeInternal(projectService, cliService, logger, types[i], token);
                    }
                }
            );

            vscode.commands.executeCommand('vscode-utmt-git.refreshTree');
        } catch (e) {
            logger.error(`导出所有资产失败：${e}`);
            vscode.window.showErrorMessage(`导出所有资产失败：${e}`);
        }
    });
    context.subscriptions.push(disposable);
}

async function exportAssetType(
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger,
    type: AssetType
): Promise<void> {
    try {
        await exportAssetTypeInternal(projectService, cliService, logger, type);
        vscode.commands.executeCommand('vscode-utmt-git.refreshTree');
    } catch (e) {
        logger.error(`导出${ASSET_TYPE_NAMES[type]}失败：${e}`);
        vscode.window.showErrorMessage(`导出${ASSET_TYPE_NAMES[type]}失败：${e}`);
    }
}

async function exportAssetTypeInternal(
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger,
    type: AssetType,
    token?: vscode.CancellationToken
): Promise<void> {
    if (!projectService.isLoaded()) {
        vscode.window.showWarningMessage('未加载 UTMT Git 项目。');
        return;
    }

    const dataWinPath = projectService.getDataWinPath();
    if (!dataWinPath || !await fileExists(dataWinPath)) {
        vscode.window.showErrorMessage(`未找到 data.win：${dataWinPath}`);
        return;
    }

    const projectRoot = projectService.getProjectRoot()!;
    const cliPath = projectService.getCliPath();
    const outputDir = projectService.getAssetDir(type);

    if (!outputDir) {
        vscode.window.showErrorMessage(`未配置"${ASSET_TYPE_NAMES[type]}"的资产目录。`);
        return;
    }

    logger.show();

    if (type === 'code') {
        const result = await cliService.exportCode(cliPath, dataWinPath, outputDir, token);
        if (result.exitCode !== 0) {
            throw new Error(`导出代码失败，退出码 ${result.exitCode}`);
        }
        vscode.window.showInformationMessage('代码导出成功！');
        return;
    }

    const scriptName = `ExportAll${capitalize(type)}Headless.csx`;
    const extensionPath = (await vscode.extensions.getExtension('utmt-git.vscode-utmt-git')?.extensionUri?.fsPath)
        || path.join(__dirname, '..', '..');
    const scriptPath = path.join(extensionPath, 'headless-scripts', scriptName);

    if (!await fileExists(scriptPath)) {
        vscode.window.showErrorMessage(`未找到导出脚本：${scriptPath}`);
        return;
    }

    const result = await cliService.runScript(
        cliPath,
        dataWinPath,
        scriptPath,
        dataWinPath,
        { [CLI_ENV_PROJECT_ROOT]: projectRoot },
        token
    );

    if (result.exitCode !== 0) {
        throw new Error(`导出${ASSET_TYPE_NAMES[type]}失败，退出码 ${result.exitCode}`);
    }

    vscode.window.showInformationMessage(`${ASSET_TYPE_NAMES[type]}导出成功！`);
}

function capitalize(s: string): string {
    return s.charAt(0).toUpperCase() + s.slice(1);
}
