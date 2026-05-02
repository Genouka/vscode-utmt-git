import * as vscode from 'vscode';
import * as path from 'path';
import { ProjectService } from '../services/projectService';
import { CliService } from '../services/cliService';

export class StatusBarProvider implements vscode.Disposable {
    private statusItem: vscode.StatusBarItem;

    constructor(projectService: ProjectService, cliService: CliService) {
        this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 50);

        projectService.onDidChange(() => {
            this.updateProjectStatus(projectService);
        });

        this.updateProjectStatus(projectService);
        this.statusItem.show();
    }

    private updateProjectStatus(projectService: ProjectService): void {
        if (projectService.isLoaded()) {
            const root = projectService.getProjectRoot();
            const name = root ? path.basename(root) : 'UTMT';
            this.statusItem.text = `$(package) ${name}`;
            this.statusItem.tooltip = 'UTMT Git：项目已加载';
            this.statusItem.command = 'vscode-utmt-git.refreshTree';
        } else {
            this.statusItem.text = '$(circle-slash) UTMT Git';
            this.statusItem.tooltip = 'UTMT Git：未加载项目，点击初始化';
            this.statusItem.command = 'vscode-utmt-git.initProject';
        }
    }

    setCliRunning(running: boolean): void {
        if (running) {
            this.statusItem.text = '$(loading~spin) UTMT Git';
            this.statusItem.tooltip = 'UTMT Git：CLI 运行中...';
        }
    }

    dispose(): void {
        this.statusItem.dispose();
    }
}
