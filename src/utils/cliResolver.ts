import * as path from 'path';
import * as os from 'os';
import * as fs from 'fs';

function getPlatformRid(): string | null {
    const platform = os.platform();
    const arch = os.arch();
    if (platform === 'win32' && (arch === 'x64' || arch === 'x86')) {
        return 'win-x64';
    }
    if (platform === 'darwin' && arch === 'x64') {
        return 'osx-x64';
    }
    if (platform === 'darwin' && arch === 'arm64') {
        return 'osx-arm64';
    }
    if (platform === 'linux' && arch === 'x64') {
        return 'linux-x64';
    }
    return null;
}

function getCliFileName(): string {
    return os.platform() === 'win32' ? 'UndertaleModCli.exe' : 'UndertaleModCli';
}

export function resolveBundledCliPath(extensionPath: string): string | null {
    const rid = getPlatformRid();
    if (!rid) {
        return null;
    }
    const cliDir = path.join(extensionPath, 'cli', rid);
    const cliFile = path.join(cliDir, getCliFileName());
    try {
        if (fs.existsSync(cliFile)) {
            return cliFile;
        }
    } catch {
        // ignore
    }
    return null;
}
