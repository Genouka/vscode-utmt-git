import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import { UtmtGitConfig, AssetType, DEFAULT_ASSET_DIRS, AssetDirs } from '../types';
import { Logger } from '../utils/logger';
import { fileExists, findConfigFile, readJson, writeJson, createDefaultConfig, resolveAssetDir, ensureDir } from '../utils/fs';
import { eventBus } from '../events';
import { ChangeTracker } from './changeTracker';
import { resolveBundledCliPath } from '../utils/cliResolver';

export class ProjectService implements vscode.Disposable {
    private config: UtmtGitConfig | undefined;
    private projectRoot: string | undefined;
    private configPath: string | undefined;
    private _onDidChange = new vscode.EventEmitter<void>();
    readonly onDidChange = this._onDidChange.event;
    private watcher: vscode.FileSystemWatcher | undefined;
    readonly changeTracker: ChangeTracker;
    private extensionPath: string;

    constructor(private logger: Logger, extensionPath: string) {
        this.changeTracker = new ChangeTracker();
        this.extensionPath = extensionPath;
    }

    async detectProject(): Promise<boolean> {
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (!workspaceFolders || workspaceFolders.length === 0) {
            return false;
        }

        for (const folder of workspaceFolders) {
            const configPath = await findConfigFile(folder.uri.fsPath);
            if (configPath) {
                await this.loadProject(path.dirname(configPath), configPath);
                return true;
            }
        }
        return false;
    }

    async loadProject(projectRoot: string, configPath?: string): Promise<void> {
        this.projectRoot = projectRoot;

        if (!configPath) {
            configPath = path.join(projectRoot, '.utmt-git.json');
        }

        if (await fileExists(configPath)) {
            this.config = await readJson<UtmtGitConfig>(configPath);
            this.configPath = configPath;
            this.logger.info(`已加载项目配置：${configPath}`);
            eventBus.emit('project-loaded', projectRoot);
        } else {
            this.config = undefined;
            this.configPath = undefined;
            this.logger.warn(`未找到配置文件：${configPath}`);
            eventBus.emit('project-unloaded');
        }

        this.setupWatcher();
        this.startChangeTracking();
        this._onDidChange.fire();
    }

    private startChangeTracking(): void {
        if (!this.config || !this.projectRoot) {
            this.changeTracker.stop();
            return;
        }
        const assetDirs = this.getAssetDirsMap();
        this.changeTracker.start(this.projectRoot, assetDirs);
    }

    private getAssetDirsMap(): Record<AssetType, string> {
        const result = {} as Record<AssetType, string>;
        const types: AssetType[] = ['code', 'sprites', 'sounds', 'objects', 'backgrounds', 'fonts'];
        for (const type of types) {
            result[type] = this.config?.assetDirs[type] ?? DEFAULT_ASSET_DIRS[type];
        }
        return result;
    }

    private setupWatcher(): void {
        this.watcher?.dispose();
        if (!this.configPath) {
            return;
        }
        this.watcher = vscode.workspace.createFileSystemWatcher(this.configPath);
        this.watcher.onDidChange(() => this.reloadConfig());
        this.watcher.onDidDelete(() => {
            this.config = undefined;
            this.configPath = undefined;
            this.changeTracker.stop();
            eventBus.emit('project-unloaded');
            this._onDidChange.fire();
        });
    }

    private async reloadConfig(): Promise<void> {
        if (!this.configPath) {
            return;
        }
        try {
            this.config = await readJson<UtmtGitConfig>(this.configPath);
            if (this.projectRoot) {
                eventBus.emit('config-changed', this.projectRoot);
            }
            this.startChangeTracking();
            this._onDidChange.fire();
            this.logger.info('项目配置已重新加载');
        } catch (e) {
            this.logger.error(`重新加载配置失败：${e}`);
        }
    }

    async saveConfig(config: UtmtGitConfig): Promise<void> {
        if (!this.projectRoot) {
            throw new Error('未设置项目根目录');
        }
        const configPath = path.join(this.projectRoot, '.utmt-git.json');
        await writeJson(configPath, config);
        this.config = config;
        this.configPath = configPath;
        eventBus.emit('config-changed', this.projectRoot);
        this._onDidChange.fire();
    }

    getConfig(): UtmtGitConfig | undefined {
        return this.config;
    }

    getProjectRoot(): string | undefined {
        return this.projectRoot;
    }

    getDataWinPath(): string | undefined {
        if (!this.config || !this.projectRoot) {
            return undefined;
        }
        return path.resolve(this.projectRoot, this.config.dataWinPath);
    }

    getCliPath(): string {
        if (this.config?.cliPath) {
            return this.config.cliPath;
        }
        const cfg = vscode.workspace.getConfiguration('vscode-utmt-git');
        const configPath = cfg.get<string>('cliPath', '');
        if (configPath && configPath !== 'UndertaleModCli') {
            return configPath;
        }
        const bundled = resolveBundledCliPath(this.extensionPath);
        if (bundled) {
            return bundled;
        }
        return 'UndertaleModCli';
    }

    getAssetDir(type: AssetType): string | undefined {
        if (!this.config || !this.projectRoot) {
            return undefined;
        }
        return resolveAssetDir(this.projectRoot, this.config.assetDirs, type);
    }

    isLoaded(): boolean {
        return this.config !== undefined;
    }

    async initializeProject(rootDir: string, dataWinPath: string, cliPath: string): Promise<void> {
        this.projectRoot = rootDir;

        const dirs = Object.values(DEFAULT_ASSET_DIRS);
        for (const dir of dirs) {
            const fullDir = path.join(rootDir, dir);
            await ensureDir(fullDir);
            const gitkeep = path.join(fullDir, '.gitkeep');
            if (!await fileExists(gitkeep)) {
                await vscode.workspace.fs.writeFile(vscode.Uri.file(gitkeep), Buffer.from(''));
            }
        }

        const config = createDefaultConfig(dataWinPath, cliPath);
        await this.saveConfig(config);
        await this.loadProject(rootDir);
        this.logger.info(`项目已初始化：${rootDir}`);
    }

    dispose(): void {
        this.watcher?.dispose();
        this.changeTracker.dispose();
        this._onDidChange.dispose();
    }
}
