import * as vscode from 'vscode';
import { AssetType } from './types';

type EventMap = {
    'project-loaded': [projectRoot: string];
    'project-unloaded': [];
    'config-changed': [projectRoot: string];
    'asset-exported': [type: AssetType, projectRoot: string];
    'asset-imported': [projectRoot: string];
    'cli-started': [command: string];
    'cli-completed': [command: string, exitCode: number];
    'asset-file-changed': [changeType: 'create' | 'change' | 'delete', uri: vscode.Uri];
};

type EventHandler<T extends unknown[]> = (...args: T) => void;

export class EventBus implements vscode.Disposable {
    private handlers = new Map<string, Set<EventHandler<unknown[]>>>();

    on<E extends keyof EventMap>(event: E, handler: EventHandler<EventMap[E]>): vscode.Disposable {
        if (!this.handlers.has(event)) {
            this.handlers.set(event, new Set());
        }
        const set = this.handlers.get(event)!;
        set.add(handler as EventHandler<unknown[]>);
        return new vscode.Disposable(() => {
            set.delete(handler as EventHandler<unknown[]>);
            if (set.size === 0) {
                this.handlers.delete(event);
            }
        });
    }

    emit<E extends keyof EventMap>(event: E, ...args: EventMap[E]): void {
        const set = this.handlers.get(event);
        if (!set) { return; }
        for (const handler of set) {
            try {
                handler(...args);
            } catch (e) {
                console.error(`Event handler error for '${event}':`, e);
            }
        }
    }

    dispose(): void {
        this.handlers.clear();
    }
}

export const eventBus = new EventBus();
