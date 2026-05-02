import * as vscode from 'vscode';

export class Logger {
    private channel: vscode.OutputChannel;

    constructor(name: string) {
        this.channel = vscode.window.createOutputChannel(name);
    }

    append(message: string): void {
        const timestamp = new Date().toLocaleTimeString();
        this.channel.appendLine(`[${timestamp}] ${message}`);
    }

    info(message: string): void {
        this.append(`[INFO] ${message}`);
    }

    warn(message: string): void {
        this.append(`[WARN] ${message}`);
    }

    error(message: string): void {
        this.append(`[ERROR] ${message}`);
    }

    show(): void {
        this.channel.show(true);
    }

    dispose(): void {
        this.channel.dispose();
    }
}
