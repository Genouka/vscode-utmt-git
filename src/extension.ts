import * as vscode from 'vscode';
import { Logger } from './utils/logger';
import { ProjectService } from './services/projectService';
import { CliService } from './services/cliService';
import { AssetTreeProvider } from './providers/assetTreeProvider';
import { InspectorProvider } from './providers/inspectorProvider';
import { StatusBarProvider } from './providers/statusBarProvider';
import { registerAllCommands } from './commands';
import { eventBus } from './events';

let logger: Logger;
let projectService: ProjectService;
let cliService: CliService;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    logger = new Logger('UTMT Git');
    projectService = new ProjectService(logger, context.extensionPath);
    cliService = new CliService(logger);

    const treeProvider = new AssetTreeProvider(projectService);
    const treeView = vscode.window.createTreeView('vscode-utmt-git.assetExplorer', {
        treeDataProvider: treeProvider,
        showCollapseAll: true,
        dragAndDropController: treeProvider,
    });

    const inspectorProvider = new InspectorProvider(projectService);
    const inspectorView = vscode.window.createTreeView('vscode-utmt-git.inspector', {
        treeDataProvider: inspectorProvider,
        showCollapseAll: true,
    });

    const statusBar = new StatusBarProvider(projectService, cliService);

    registerAllCommands(context, projectService, cliService, logger);

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.refreshTree', () => {
            treeProvider.refresh();
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.filterAssets', () => {
            treeProvider.promptFilter();
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.clearFilter', () => {
            treeProvider.clearFilter();
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.navigateToAsset', async (assetFilePath: string) => {
            if (!assetFilePath) { return; }

            const item = await treeProvider.findByFilePath(assetFilePath);
            if (item) {
                await treeView.reveal(item, {
                    select: true,
                    focus: true,
                    expand: true,
                });
                inspectorProvider.inspect(item);
            }
        })
    );

    context.subscriptions.push(
        eventBus.on('asset-file-changed', () => {
            treeProvider.refresh();
        })
    );

    context.subscriptions.push(
        eventBus.on('asset-exported', () => {
            treeProvider.refresh();
        })
    );

    context.subscriptions.push(
        eventBus.on('asset-imported', () => {
            treeProvider.refresh();
        })
    );

    context.subscriptions.push(
        eventBus.on('cli-started', () => {
            statusBar.setCliRunning(true);
        })
    );

    context.subscriptions.push(
        eventBus.on('cli-completed', () => {
            statusBar.setCliRunning(false);
        })
    );

    treeView.onDidChangeSelection((e) => {
        if (e.selection.length > 0) {
            inspectorProvider.inspect(e.selection[0]);
        }
    });

    context.subscriptions.push(logger);
    context.subscriptions.push(projectService);
    context.subscriptions.push(cliService);
    context.subscriptions.push(treeView);
    context.subscriptions.push(inspectorView);
    context.subscriptions.push(statusBar);
    context.subscriptions.push(eventBus);

    const detected = await projectService.detectProject();
    vscode.commands.executeCommand('setContext', 'vscode-utmt-git.projectLoaded', detected);

    if (detected) {
        logger.info('UTMT Git 项目已检测并加载');
    } else {
        logger.info('工作区中未检测到 UTMT Git 项目');
    }
}

export function deactivate(): void {
    projectService?.dispose();
    cliService?.dispose();
    logger?.dispose();
}
