import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { AssetTreeItem } from '../providers/assetTreeProvider';
import { ProjectService } from '../services/projectService';
import { Logger } from '../utils/logger';

export function registerAssetOperations(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    logger: Logger
): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.deleteAsset', async (item: AssetTreeItem) => {
            if (!item.filePath) { return; }

            const name = path.basename(item.filePath);
            const confirm = await vscode.window.showWarningMessage(
                `确定要删除"${name}"吗？`,
                { modal: true },
                '删除'
            );
            if (confirm !== '删除') { return; }

            try {
                const stat = fs.statSync(item.filePath);
                if (stat.isDirectory()) {
                    await vscode.workspace.fs.delete(vscode.Uri.file(item.filePath), { recursive: true });
                } else {
                    await vscode.workspace.fs.delete(vscode.Uri.file(item.filePath));
                }
                logger.info(`已删除：${item.filePath}`);
            } catch (e) {
                vscode.window.showErrorMessage(`删除"${name}"失败：${e}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.renameAsset', async (item: AssetTreeItem) => {
            if (!item.filePath) { return; }

            const oldName = path.basename(item.filePath);
            const newName = await vscode.window.showInputBox({
                prompt: `重命名"${oldName}"`,
                value: oldName,
                validateInput: (value) => {
                    if (!value || value.trim().length === 0) { return '名称不能为空'; }
                    if (value.includes('/') || value.includes('\\')) { return '名称不能包含路径分隔符'; }
                    return undefined;
                },
            });

            if (!newName || newName === oldName) { return; }

            const dir = path.dirname(item.filePath);
            const newPath = path.join(dir, newName);

            try {
                await vscode.workspace.fs.rename(
                    vscode.Uri.file(item.filePath),
                    vscode.Uri.file(newPath),
                    { overwrite: false }
                );
                logger.info(`已重命名：${oldName} -> ${newName}`);
            } catch (e) {
                vscode.window.showErrorMessage(`重命名"${oldName}"失败：${e}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.newScript', async (item: AssetTreeItem) => {
            if (!projectService.isLoaded()) { return; }

            const codeDir = projectService.getAssetDir('code');
            if (!codeDir) { return; }

            const targetDir = (item?.filePath && fs.statSync(item.filePath).isDirectory())
                ? item.filePath
                : codeDir;

            const name = await vscode.window.showInputBox({
                prompt: '新脚本名称',
                placeHolder: '例如：gml_Script_my_script',
                validateInput: (value) => {
                    if (!value || value.trim().length === 0) { return '名称不能为空'; }
                    if (!value.endsWith('.gml')) {
                        return '脚本名称必须以 .gml 结尾';
                    }
                    return undefined;
                },
            });

            if (!name) { return; }

            const filePath = path.join(targetDir, name);
            if (fs.existsSync(filePath)) {
                vscode.window.showErrorMessage(`文件已存在：${name}`);
                return;
            }

            try {
                await vscode.workspace.fs.writeFile(vscode.Uri.file(filePath), Buffer.from(''));
                const doc = await vscode.workspace.openTextDocument(filePath);
                await vscode.window.showTextDocument(doc);
                logger.info(`已创建脚本：${filePath}`);
            } catch (e) {
                vscode.window.showErrorMessage(`创建脚本失败：${e}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.newFolder', async (item: AssetTreeItem) => {
            if (!item.filePath) { return; }

            const name = await vscode.window.showInputBox({
                prompt: '新文件夹名称',
                validateInput: (value) => {
                    if (!value || value.trim().length === 0) { return '名称不能为空'; }
                    if (value.includes('/') || value.includes('\\')) { return '名称不能包含路径分隔符'; }
                    return undefined;
                },
            });

            if (!name) { return; }

            const folderPath = path.join(item.filePath, name);

            try {
                await vscode.workspace.fs.createDirectory(vscode.Uri.file(folderPath));
                logger.info(`已创建文件夹：${folderPath}`);
            } catch (e) {
                vscode.window.showErrorMessage(`创建文件夹失败：${e}`);
            }
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('vscode-utmt-git.copyPath', async (item: AssetTreeItem) => {
            if (!item.filePath) { return; }

            await vscode.env.clipboard.writeText(item.filePath);
            vscode.window.showInformationMessage(`已复制路径：${item.filePath}`);
        })
    );
}
