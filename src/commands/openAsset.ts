import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

export function registerOpenAsset(
    context: vscode.ExtensionContext
): void {
    const disposable = vscode.commands.registerCommand('vscode-utmt-git.openAsset', async (node?: AssetNode) => {
        if (!node || !node.filePath) {
            return;
        }

        const filePath = node.filePath;
        if (!fs.existsSync(filePath)) {
            vscode.window.showErrorMessage(`未找到文件：${filePath}`);
            return;
        }

        const stat = fs.statSync(filePath);
        if (stat.isDirectory()) {
            await vscode.commands.executeCommand('revealInExplorer', vscode.Uri.file(filePath));
            return;
        }

        const ext = path.extname(filePath).toLowerCase();

        if (['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp'].includes(ext)) {
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(filePath));
            await vscode.window.showTextDocument(doc, { preview: true });
        } else {
            const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(filePath));
            await vscode.window.showTextDocument(doc, { preview: false });
        }
    });

    context.subscriptions.push(disposable);

    const revealDisposable = vscode.commands.registerCommand('vscode-utmt-git.revealInExplorer', async (node?: AssetNode) => {
        if (!node || !node.filePath) {
            return;
        }
        await vscode.commands.executeCommand('revealInExplorer', vscode.Uri.file(node.filePath));
    });

    context.subscriptions.push(revealDisposable);
}

export interface AssetNode {
    filePath: string;
}
