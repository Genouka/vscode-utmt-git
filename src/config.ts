import * as vscode from 'vscode';
import * as path from 'path';
import * as os from 'os';
import { resolveBundledCliPath } from './utils/cliResolver';

const SECTION = 'vscode-utmt-git';

function cfg<T>(key: string, defaultValue: T): T {
    return vscode.workspace.getConfiguration(SECTION).get<T>(key, defaultValue);
}

function getDefaultCliPath(): string {
    const ext = vscode.extensions.getExtension('utmt-git.vscode-utmt-git');
    if (ext) {
        const bundled = resolveBundledCliPath(ext.extensionUri.fsPath);
        if (bundled) {
            return bundled;
        }
    }
    return 'UndertaleModCli';
}

export const utmtConfig = {
    get cliPath(): string {
        const configVal = cfg('cliPath', '');
        if (configVal && configVal !== 'UndertaleModCli') {
            return configVal;
        }
        return getDefaultCliPath();
    },
    get dataWinPath(): string {
        return cfg('dataWinPath', 'data.win');
    },
    get autoExportOnSave(): boolean {
        return cfg('autoExportOnSave', false);
    },
    get autoImportBeforeRun(): boolean {
        return cfg('autoImportBeforeRun', false);
    },
    get diagnosticsEnable(): boolean {
        return cfg('diagnostics.enable', true);
    },
    get treeShowFileCount(): boolean {
        return cfg('tree.showFileCount', true);
    },
    get treeOpenFoldersOnFilter(): boolean {
        return cfg('tree.openFoldersOnFilter', true);
    },
    get editingReprocessDelay(): number {
        return cfg('editing.reprocessDelay', 50);
    },
    get externalChangeDelay(): number {
        return cfg('externalChangeDelay', 100);
    },
};
