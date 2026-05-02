import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { ProjectService } from '../services/projectService';
import { AssetType, ASSET_TYPE_LABELS } from '../types';
import { utmtConfig } from '../config';

const TREE_MIME_TYPE = 'application/vnd.code.tree.vscode-utmt-git.assetExplorer';

export class AssetTreeProvider implements vscode.TreeDataProvider<AssetTreeItem>, vscode.TreeDragAndDropController<AssetTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<AssetTreeItem | undefined | null>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    readonly dragMimeTypes = [TREE_MIME_TYPE];
    readonly dropMimeTypes = [TREE_MIME_TYPE, 'text/uri-list'];

    private filterQuery: string | undefined;
    private itemCache = new Map<string, AssetTreeItem>();
    private parentMap = new Map<AssetTreeItem, AssetTreeItem>();

    constructor(private projectService: ProjectService) {
        this.projectService.onDidChange(() => this.refresh());
    }

    refresh(): void {
        this.itemCache.clear();
        this.parentMap.clear();
        this._onDidChangeTreeData.fire(undefined);
    }

    getParent(element: AssetTreeItem): vscode.ProviderResult<AssetTreeItem> {
        return this.parentMap.get(element) ?? null;
    }

    async findByFilePath(filePath: string): Promise<AssetTreeItem | undefined> {
        const cached = this.itemCache.get(filePath);
        if (cached) {
            return cached;
        }

        const types: AssetType[] = ['code', 'sprites', 'sounds', 'objects', 'backgrounds', 'fonts'];
        for (const type of types) {
            const dir = this.projectService.getAssetDir(type);
            if (!dir || !fs.existsSync(dir)) { continue; }

            const typeItems = await this.getChildren(undefined);
            const typeItem = typeItems.find(i => i.contextValue === `assetType-${type}`);
            if (!typeItem) { continue; }

            const found = await this.searchInChildren(typeItem, filePath);
            if (found) {
                return found;
            }
        }

        return undefined;
    }

    private async searchInChildren(parent: AssetTreeItem, filePath: string): Promise<AssetTreeItem | undefined> {
        const children = await this.getChildren(parent);
        for (const child of children) {
            if (child.filePath === filePath) {
                return child;
            }
            if (child.collapsibleState !== vscode.TreeItemCollapsibleState.None) {
                const found = await this.searchInChildren(child, filePath);
                if (found) { return found; }
            }
        }
        return undefined;
    }

    async promptFilter(): Promise<void> {
        const query = await vscode.window.showInputBox({
            prompt: '按名称筛选资产（支持正则表达式）',
            value: this.filterQuery ?? '',
            placeHolder: '例如：spr_player, obj_.*',
        });
        if (query !== undefined) {
            this.filterQuery = query.length > 0 ? query : undefined;
            this.refresh();
        }
    }

    clearFilter(): void {
        this.filterQuery = undefined;
        this.refresh();
    }

    getTreeItem(element: AssetTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: AssetTreeItem): Promise<AssetTreeItem[]> {
        if (!this.projectService.isLoaded()) {
            return [];
        }

        let children: AssetTreeItem[];

        if (!element) {
            children = this.getRootItems();
        } else if (element.contextValue?.startsWith('assetType')) {
            children = this.getAssetTypeItems(element.metadata?.type as AssetType, element.filePath!);
        } else if (element.contextValue === 'assetFolder' || element.contextValue === 'asset-sprite-dir' || element.contextValue === 'asset-sound-dir') {
            children = this.getFolderItems(element.filePath!);
        } else {
            children = [];
        }

        if (element) {
            for (const child of children) {
                this.parentMap.set(child, element);
            }
        }

        return children;
    }

    private getFilter(): RegExp | undefined {
        if (!this.filterQuery || this.filterQuery.length === 0) { return undefined; }
        try {
            return new RegExp(this.filterQuery, 'i');
        } catch {
            return undefined;
        }
    }

    private getRootItems(): AssetTreeItem[] {
        const types: AssetType[] = ['code', 'sprites', 'sounds', 'objects', 'backgrounds', 'fonts'];
        const items: AssetTreeItem[] = [];

        const filter = this.getFilter();

        if (this.filterQuery) {
            const filterItem = new AssetTreeItem(
                `筛选："${this.filterQuery}"`,
                vscode.TreeItemCollapsibleState.None,
                'tree-filter-enabled',
                undefined
            );
            filterItem.iconPath = new vscode.ThemeIcon('filter');
            filterItem.description = '点击清除';
            filterItem.command = {
                command: 'vscode-utmt-git.clearFilter',
                title: '清除筛选',
            };
            items.push(filterItem);
        }

        for (const type of types) {
            const dir = this.projectService.getAssetDir(type);
            if (dir && fs.existsSync(dir)) {
                if (filter && !this.hasMatchingAssets(dir, filter, type)) {
                    continue;
                }

                const showCount = utmtConfig.treeShowFileCount;
                const fileCount = this.countFilesRecursive(dir);
                const label = showCount ? `${ASSET_TYPE_LABELS[type]} (${fileCount})` : ASSET_TYPE_LABELS[type];
                const item = new AssetTreeItem(
                    label,
                    vscode.TreeItemCollapsibleState.Collapsed,
                    `assetType-${type}`,
                    dir
                );
                item.iconPath = this.getAssetTypeIcon(type);
                item.metadata = { type };
                item.description = ASSET_TYPE_LABELS[type];
                items.push(item);
            }
        }

        return items;
    }

    private hasMatchingAssets(dir: string, filter: RegExp, type: AssetType): boolean {
        try {
            const entries = fs.readdirSync(dir, { withFileTypes: true });
            for (const entry of entries) {
                if (entry.name === '.gitkeep') { continue; }
                if (filter.test(entry.name)) { return true; }
                if (entry.isDirectory()) {
                    if (this.hasMatchingAssets(path.join(dir, entry.name), filter, type)) {
                        return true;
                    }
                }
            }
        } catch { /* ignore */ }
        return false;
    }

    private getAssetTypeIcon(type: AssetType): vscode.ThemeIcon {
        switch (type) {
            case 'code': return new vscode.ThemeIcon('file-code');
            case 'sprites': return new vscode.ThemeIcon('symbol-color');
            case 'sounds': return new vscode.ThemeIcon('unmute');
            case 'objects': return new vscode.ThemeIcon('symbol-class');
            case 'backgrounds': return new vscode.ThemeIcon('symbol-file');
            case 'fonts': return new vscode.ThemeIcon('symbol-font');
            default: return new vscode.ThemeIcon('folder');
        }
    }

    private getAssetTypeItems(type: AssetType, dir: string): AssetTreeItem[] {
        if (!fs.existsSync(dir)) {
            return [];
        }

        try {
            if (type === 'code') {
                return this.getCodeItems(dir);
            }

            if (type === 'objects') {
                return this.getObjectItems(dir);
            }

            return this.getContainerItems(dir, type);
        } catch {
            return [];
        }
    }

    private getCodeItems(dir: string): AssetTreeItem[] {
        const items: AssetTreeItem[] = [];
        const filter = this.getFilter();

        const directGml = fs.readdirSync(dir).filter(f => f.endsWith('.gml'));
        for (const name of directGml) {
            if (filter && !filter.test(name)) { continue; }
            const fullPath = path.join(dir, name);
            const item = new AssetTreeItem(name, vscode.TreeItemCollapsibleState.None, 'asset-file-gml', fullPath);
            item.iconPath = new vscode.ThemeIcon('file-code');
            item.command = {
                command: 'vscode-utmt-git.openAsset',
                title: 'Open',
                arguments: [item],
            };
            item.resourceUri = vscode.Uri.file(fullPath);
            items.push(item);
        }

        const subdirs = fs.readdirSync(dir, { withFileTypes: true })
            .filter(e => e.isDirectory() && e.name !== '.gitkeep');

        for (const subdir of subdirs) {
            if (filter && !filter.test(subdir.name)) {
                const subPath = path.join(dir, subdir.name);
                if (!this.hasMatchingAssets(subPath, filter, 'code')) { continue; }
            }
            const subPath = path.join(dir, subdir.name);
            const gmlCount = this.countFilesRecursive(subPath, '.gml');
            const showCount = utmtConfig.treeShowFileCount;
            const label = showCount ? `${subdir.name} (${gmlCount})` : subdir.name;
            const item = new AssetTreeItem(
                label,
                vscode.TreeItemCollapsibleState.Collapsed,
                'assetFolder',
                subPath
            );
            item.iconPath = new vscode.ThemeIcon('folder');
            items.push(item);
        }

        return this.sortItems(items);
    }

    private getObjectItems(dir: string): AssetTreeItem[] {
        const items: AssetTreeItem[] = [];
        const filter = this.getFilter();

        const entries = fs.readdirSync(dir, { withFileTypes: true });
        for (const entry of entries) {
            if (filter && !filter.test(entry.name)) { continue; }

            if (entry.isFile() && entry.name.endsWith('.json')) {
                const fullPath = path.join(dir, entry.name);
                const item = new AssetTreeItem(entry.name, vscode.TreeItemCollapsibleState.None, 'asset-object-json', fullPath);
                item.iconPath = new vscode.ThemeIcon('json');
                item.command = {
                    command: 'vscode-utmt-git.openAsset',
                    title: 'Open',
                    arguments: [item],
                };
                item.resourceUri = vscode.Uri.file(fullPath);
                items.push(item);
            } else if (entry.isDirectory() && entry.name !== '.gitkeep') {
                const subPath = path.join(dir, entry.name);
                if (filter && !this.hasMatchingAssets(subPath, filter, 'objects')) { continue; }
                const jsonCount = this.countFilesRecursive(subPath, '.json');
                const showCount = utmtConfig.treeShowFileCount;
                const label = showCount ? `${entry.name} (${jsonCount})` : entry.name;
                const item = new AssetTreeItem(
                    label,
                    vscode.TreeItemCollapsibleState.Collapsed,
                    'assetFolder',
                    subPath
                );
                item.iconPath = new vscode.ThemeIcon('folder');
                items.push(item);
            }
        }

        return this.sortItems(items);
    }

    private getContainerItems(dir: string, type: AssetType): AssetTreeItem[] {
        const entries = fs.readdirSync(dir, { withFileTypes: true });
        const items: AssetTreeItem[] = [];
        const filter = this.getFilter();

        for (const entry of entries) {
            if (entry.name === '.gitkeep') { continue; }
            if (filter && !filter.test(entry.name)) {
                const fullPath = path.join(dir, entry.name);
                if (entry.isDirectory() && !this.hasMatchingAssets(fullPath, filter, type)) { continue; }
                if (entry.isFile()) { continue; }
            }

            const fullPath = path.join(dir, entry.name);

            if (entry.isDirectory()) {
                const contextValue = this.getDirectoryContextValue(fullPath, type);
                const fileCount = this.countFilesRecursive(fullPath);
                const showCount = utmtConfig.treeShowFileCount;
                const label = showCount ? `${entry.name} (${fileCount})` : entry.name;
                const item = new AssetTreeItem(
                    label,
                    vscode.TreeItemCollapsibleState.Collapsed,
                    contextValue,
                    fullPath
                );
                item.iconPath = this.getDirectoryIcon(fullPath, type);
                items.push(item);
            } else {
                items.push(this.createFileItem(entry.name, fullPath, type));
            }
        }

        return this.sortItems(items);
    }

    private getDirectoryContextValue(dirPath: string, type: AssetType): string {
        if (type === 'sprites' && this.hasMetadataJson(dirPath)) {
            return 'asset-sprite-dir';
        }
        if (type === 'sounds' && this.hasMetadataJson(dirPath)) {
            return 'asset-sound-dir';
        }
        return 'assetFolder';
    }

    private hasMetadataJson(dirPath: string): boolean {
        try {
            const entries = fs.readdirSync(dirPath);
            return entries.includes('metadata.json');
        } catch {
            return false;
        }
    }

    private getDirectoryIcon(dirPath: string, type: AssetType): vscode.ThemeIcon {
        if (type === 'sprites' && this.hasMetadataJson(dirPath)) {
            return new vscode.ThemeIcon('symbol-color');
        }
        if (type === 'sounds' && this.hasMetadataJson(dirPath)) {
            return new vscode.ThemeIcon('unmute');
        }
        return new vscode.ThemeIcon('folder');
    }

    private getFolderItems(dirPath: string): AssetTreeItem[] {
        if (!fs.existsSync(dirPath)) {
            return [];
        }

        try {
            const entries = fs.readdirSync(dirPath, { withFileTypes: true });
            const items: AssetTreeItem[] = [];
            const filter = this.getFilter();

            for (const entry of entries) {
                if (entry.name === '.gitkeep') { continue; }
                if (filter && !filter.test(entry.name)) { continue; }

                const fullPath = path.join(dirPath, entry.name);

                if (entry.isDirectory()) {
                    const fileCount = this.countFilesRecursive(fullPath);
                    const showCount = utmtConfig.treeShowFileCount;
                    const label = showCount ? `${entry.name} (${fileCount})` : entry.name;
                    const item = new AssetTreeItem(
                        label,
                        vscode.TreeItemCollapsibleState.Collapsed,
                        'assetFolder',
                        fullPath
                    );
                    item.iconPath = new vscode.ThemeIcon('folder');
                    items.push(item);
                } else {
                    const ext = path.extname(entry.name).toLowerCase();
                    const contextValue = this.getFileContextValue(ext);
                    const item = new AssetTreeItem(entry.name, vscode.TreeItemCollapsibleState.None, contextValue, fullPath);
                    item.iconPath = this.getFileIcon(ext);
                    item.command = {
                        command: 'vscode-utmt-git.openAsset',
                        title: 'Open',
                        arguments: [item],
                    };
                    item.resourceUri = vscode.Uri.file(fullPath);
                    items.push(item);
                }
            }

            return this.sortItems(items);
        } catch {
            return [];
        }
    }

    private getFileContextValue(ext: string): string {
        if (ext === '.gml') { return 'asset-file-gml'; }
        if (['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp'].includes(ext)) { return 'asset-file-png'; }
        if (ext === '.json') { return 'asset-file-json'; }
        if (['.ogg', '.wav', '.mp3'].includes(ext)) { return 'asset-file-audio'; }
        return 'file';
    }

    private createFileItem(name: string, fullPath: string, type: AssetType): AssetTreeItem {
        const ext = path.extname(name).toLowerCase();
        const contextValue = this.getFileContextValue(ext);
        const item = new AssetTreeItem(name, vscode.TreeItemCollapsibleState.None, contextValue, fullPath);
        item.iconPath = this.getFileIcon(ext);
        item.command = {
            command: 'vscode-utmt-git.openAsset',
            title: 'Open',
            arguments: [item],
        };
        item.resourceUri = vscode.Uri.file(fullPath);
        return item;
    }

    private getFileIcon(ext: string): vscode.ThemeIcon {
        if (['.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp'].includes(ext)) {
            return new vscode.ThemeIcon('file-media');
        }
        if (ext === '.json') {
            return new vscode.ThemeIcon('json');
        }
        if (ext === '.gml') {
            return new vscode.ThemeIcon('file-code');
        }
        if (['.ogg', '.wav', '.mp3'].includes(ext)) {
            return new vscode.ThemeIcon('file-media');
        }
        return new vscode.ThemeIcon('file');
    }

    private sortItems(items: AssetTreeItem[]): AssetTreeItem[] {
        return items.sort((a, b) => {
            const aIsDir = a.contextValue === 'assetFolder' || a.contextValue === 'asset-sprite-dir' || a.contextValue === 'asset-sound-dir' ? 0 : 1;
            const bIsDir = b.contextValue === 'assetFolder' || b.contextValue === 'asset-sprite-dir' || b.contextValue === 'asset-sound-dir' ? 0 : 1;
            if (aIsDir !== bIsDir) { return aIsDir - bIsDir; }
            return String(a.label).localeCompare(String(b.label));
        });
    }

    private countFilesRecursive(dir: string, ext?: string): number {
        try {
            if (!fs.existsSync(dir)) { return 0; }
            let count = 0;
            const entries = fs.readdirSync(dir, { withFileTypes: true });
            for (const entry of entries) {
                if (entry.name === '.gitkeep') { continue; }
                if (entry.isDirectory()) {
                    count += this.countFilesRecursive(path.join(dir, entry.name), ext);
                } else if (entry.isFile()) {
                    if (!ext || path.extname(entry.name).toLowerCase() === ext) {
                        count++;
                    }
                }
            }
            return count;
        } catch {
            return 0;
        }
    }

    async handleDrag(source: AssetTreeItem[], dataTransfer: vscode.DataTransfer): Promise<void> {
        dataTransfer.set(TREE_MIME_TYPE, new vscode.DataTransferItem(source));
    }

    async handleDrop(target: AssetTreeItem | undefined, dataTransfer: vscode.DataTransfer): Promise<void> {
        const externalFiles = dataTransfer.get('text/uri-list');
        if (externalFiles) {
            const uris = externalFiles.value as string[];
            if (uris && uris.length > 0) {
                await this.handleExternalFileDrop(target, uris);
                return;
            }
        }

        const internalItems = dataTransfer.get(TREE_MIME_TYPE);
        if (internalItems) {
            const items = internalItems.value as AssetTreeItem[];
            if (items && items.length > 0 && target) {
                await this.handleInternalDrop(target, items);
            }
        }
    }

    private async handleExternalFileDrop(target: AssetTreeItem | undefined, uris: string[]): Promise<void> {
        const targetDir = this.resolveDropTarget(target);
        if (!targetDir) { return; }

        for (const uriStr of uris) {
            const uri = vscode.Uri.parse(uriStr);
            const srcPath = uri.fsPath;
            const fileName = path.basename(srcPath);
            const destPath = path.join(targetDir, fileName);

            try {
                await vscode.workspace.fs.copy(uri, vscode.Uri.file(destPath), { overwrite: true });
            } catch (e) {
                vscode.window.showErrorMessage(`复制 ${fileName} 失败：${e}`);
            }
        }

        this.refresh();
    }

    private async handleInternalDrop(target: AssetTreeItem, items: AssetTreeItem[]): Promise<void> {
        const targetDir = this.resolveDropTarget(target);
        if (!targetDir) { return; }

        for (const item of items) {
            if (!item.filePath) { continue; }
            const fileName = path.basename(item.filePath);
            const destPath = path.join(targetDir, fileName);

            if (item.filePath === destPath) { continue; }

            try {
                await vscode.workspace.fs.rename(
                    vscode.Uri.file(item.filePath),
                    vscode.Uri.file(destPath),
                    { overwrite: false }
                );
            } catch (e) {
                vscode.window.showErrorMessage(`移动 ${fileName} 失败：${e}`);
            }
        }

        this.refresh();
    }

    private resolveDropTarget(target: AssetTreeItem | undefined): string | undefined {
        if (!target) { return undefined; }

        if (target.filePath && fs.existsSync(target.filePath)) {
            const stat = fs.statSync(target.filePath);
            if (stat.isDirectory()) {
                return target.filePath;
            }
            return path.dirname(target.filePath);
        }

        return undefined;
    }
}

export class AssetTreeItem extends vscode.TreeItem {
    filePath?: string;
    metadata?: { type: AssetType };

    constructor(
        label: string,
        collapsibleState: vscode.TreeItemCollapsibleState,
        contextValue: string,
        filePath?: string
    ) {
        super(label, collapsibleState);
        this.contextValue = contextValue;
        this.filePath = filePath;
        this.tooltip = filePath;
    }
}
