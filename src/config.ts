import * as vscode from 'vscode';

const SECTION = 'vscode-utmt-git';

function cfg<T>(key: string, defaultValue: T): T {
    return vscode.workspace.getConfiguration(SECTION).get<T>(key, defaultValue);
}

export const utmtConfig = {
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
