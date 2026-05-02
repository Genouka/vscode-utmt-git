import * as vscode from 'vscode';
import { eventBus } from '../events';
import { utmtConfig } from '../config';
import { AssetType } from '../types';

interface ChangeEvent {
    type: 'create' | 'change' | 'delete';
    uri: vscode.Uri;
}

const PRIORITY_EXTENSIONS = ['.json', '.gml', '.png', '.ogg', '.wav', '.mp3'];

export class ChangeTracker implements vscode.Disposable {
    private queue: ChangeEvent[] = [];
    private timeout: ReturnType<typeof setTimeout> | undefined;
    private watchers: vscode.FileSystemWatcher[] = [];

    start(projectRoot: string, assetDirs: Record<AssetType, string>): void {
        this.stop();

        for (const type of Object.keys(assetDirs) as AssetType[]) {
            const dir = assetDirs[type];
            const pattern = new vscode.RelativePattern(
                vscode.Uri.file(projectRoot),
                `${dir}/**/*`
            );
            const watcher = vscode.workspace.createFileSystemWatcher(pattern);
            watcher.onDidCreate((uri) => this.addChange({ type: 'create', uri }));
            watcher.onDidChange((uri) => this.addChange({ type: 'change', uri }));
            watcher.onDidDelete((uri) => this.addChange({ type: 'delete', uri }));
            this.watchers.push(watcher);
        }
    }

    stop(): void {
        for (const w of this.watchers) {
            w.dispose();
        }
        this.watchers = [];
        if (this.timeout) {
            clearTimeout(this.timeout);
            this.timeout = undefined;
        }
        this.queue = [];
    }

    private addChange(event: ChangeEvent): void {
        this.queue.push(event);
        if (this.timeout) {
            clearTimeout(this.timeout);
        }
        this.timeout = setTimeout(
            () => this.flush(),
            utmtConfig.externalChangeDelay
        );
    }

    private flush(): void {
        this.timeout = undefined;
        if (this.queue.length === 0) { return; }

        const sorted = [...this.queue].sort((a, b) => {
            for (const ext of PRIORITY_EXTENSIONS) {
                const aExt = a.uri.path.endsWith(ext);
                const bExt = b.uri.path.endsWith(ext);
                if (aExt && !bExt) { return -1; }
                if (bExt && !aExt) { return 1; }
            }
            return 0;
        });

        const deduped = this.deduplicate(sorted);

        this.queue = [];

        for (const event of deduped) {
            eventBus.emit('asset-file-changed', event.type, event.uri);
        }
    }

    private deduplicate(events: ChangeEvent[]): ChangeEvent[] {
        const latest = new Map<string, ChangeEvent>();
        for (const event of events) {
            latest.set(event.uri.toString(), event);
        }
        return Array.from(latest.values());
    }

    dispose(): void {
        this.stop();
    }
}
