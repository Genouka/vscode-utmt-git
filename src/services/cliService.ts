import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as vscode from 'vscode';
import { Logger } from '../utils/logger';
import { CLI_ENV_PROJECT_ROOT } from '../types';
import { eventBus } from '../events';

export interface CliResult {
    exitCode: number;
    stdout: string;
    stderr: string;
}

function quoteIfSpaced(arg: string): string {
    if (arg.includes(' ')) {
        return `"${arg}"`;
    }
    return arg;
}

export class CliService implements vscode.Disposable {
    private runningProcess: ChildProcess | undefined;
    private logger: Logger;

    constructor(logger: Logger) {
        this.logger = logger;
    }

    async execute(
        cliPath: string,
        args: string[],
        options?: {
            env?: Record<string, string>;
            cwd?: string;
            onStdout?: (data: string) => void;
            onStderr?: (data: string) => void;
            token?: vscode.CancellationToken;
        }
    ): Promise<CliResult> {
        return new Promise((resolve, reject) => {
            const env = { ...process.env, ...options?.env };
            const cwd = options?.cwd;

            const quotedArgs = args.map(quoteIfSpaced);
            const commandStr = `${quoteIfSpaced(cliPath)} ${quotedArgs.join(' ')}`;
            this.logger.info(`CLI: ${commandStr}`);
            eventBus.emit('cli-started', commandStr);

            const proc = spawn(quoteIfSpaced(cliPath), quotedArgs, { env, cwd, shell: true });
            this.runningProcess = proc;

            let stdout = '';
            let stderr = '';

            proc.stdout?.on('data', (data: Buffer) => {
                const text = data.toString();
                stdout += text;
                this.logger.append(text);
                options?.onStdout?.(text);
            });

            proc.stderr?.on('data', (data: Buffer) => {
                const text = data.toString();
                stderr += text;
                this.logger.append(text);
                options?.onStderr?.(text);
            });

            const disposable = options?.token?.onCancellationRequested(() => {
                this.kill();
            });

            proc.on('close', (code) => {
                this.runningProcess = undefined;
                disposable?.dispose();
                eventBus.emit('cli-completed', commandStr, code ?? 1);
                resolve({
                    exitCode: code ?? 1,
                    stdout,
                    stderr,
                });
            });

            proc.on('error', (err) => {
                this.runningProcess = undefined;
                disposable?.dispose();
                eventBus.emit('cli-completed', commandStr, 1);
                reject(err);
            });
        });
    }

    async importAll(cliPath: string, dataWinPath: string, scriptPath: string, projectRoot: string, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = [
            'load', dataWinPath,
            '-s', scriptPath,
            '-o', dataWinPath,
        ];
        const result = await this.execute(cliPath, args, {
            env: { [CLI_ENV_PROJECT_ROOT]: projectRoot },
            cwd: path.dirname(dataWinPath),
            token,
        });
        if (result.exitCode === 0) {
            eventBus.emit('asset-imported', projectRoot);
        }
        return result;
    }

    async exportCode(cliPath: string, dataWinPath: string, outputDir: string, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = [
            'dump', dataWinPath,
            '-c', 'UMT_DUMP_ALL',
            '-o', outputDir,
        ];
        const result = await this.execute(cliPath, args, {
            cwd: path.dirname(dataWinPath),
            token,
        });
        if (result.exitCode === 0) {
            eventBus.emit('asset-exported', 'code', path.dirname(dataWinPath));
        }
        return result;
    }

    async replaceCode(cliPath: string, dataWinPath: string, codeDir: string, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = [
            'replace', dataWinPath,
            '-c', `UMT_REPLACE_ALL=${codeDir}`,
            '-o', dataWinPath,
        ];
        return this.execute(cliPath, args, {
            cwd: path.dirname(dataWinPath),
            token,
        });
    }

    async runScript(cliPath: string, dataWinPath: string, scriptPath: string, outputPath: string, env?: Record<string, string>, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = [
            'load', dataWinPath,
            '-s', scriptPath,
            '-o', outputPath,
        ];
        return this.execute(cliPath, args, {
            env,
            cwd: path.dirname(dataWinPath),
            token,
        });
    }

    async dumpTextures(cliPath: string, dataWinPath: string, outputDir: string, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = [
            'dump', dataWinPath,
            '-t',
            '-o', outputDir,
        ];
        return this.execute(cliPath, args, {
            cwd: path.dirname(dataWinPath),
            token,
        });
    }

    async info(cliPath: string, dataWinPath: string, token?: vscode.CancellationToken): Promise<CliResult> {
        const args = ['info', dataWinPath];
        return this.execute(cliPath, args, {
            cwd: path.dirname(dataWinPath),
            token,
        });
    }

    kill(): void {
        if (this.runningProcess && !this.runningProcess.killed) {
            this.runningProcess.kill();
            this.runningProcess = undefined;
            this.logger.warn('CLI 进程已终止');
        }
    }

    isRunning(): boolean {
        return this.runningProcess !== undefined && !this.runningProcess.killed;
    }

    dispose(): void {
        this.kill();
    }
}
