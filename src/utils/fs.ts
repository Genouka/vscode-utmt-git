import * as fs from 'fs';
import * as path from 'path';
import { UtmtGitConfig, AssetDirs, DEFAULT_ASSET_DIRS, CONFIG_FILE_NAME } from '../types';

export function fileExists(filePath: string): Promise<boolean> {
    return fs.promises.access(filePath, fs.constants.F_OK).then(() => true, () => false);
}

export function dirExists(dirPath: string): Promise<boolean> {
    return fs.promises.access(dirPath, fs.constants.F_OK).then(() => true, () => false);
}

export async function ensureDir(dirPath: string): Promise<void> {
    await fs.promises.mkdir(dirPath, { recursive: true });
}

export async function writeJson(filePath: string, data: unknown): Promise<void> {
    await ensureDir(path.dirname(filePath));
    await fs.promises.writeFile(filePath, JSON.stringify(data, null, 2), 'utf-8');
}

export async function readJson<T>(filePath: string): Promise<T> {
    const content = await fs.promises.readFile(filePath, 'utf-8');
    return JSON.parse(content) as T;
}

export async function findConfigFile(workspaceRoot: string): Promise<string | undefined> {
    const candidates = [
        path.join(workspaceRoot, CONFIG_FILE_NAME),
    ];
    for (const candidate of candidates) {
        if (await fileExists(candidate)) {
            return candidate;
        }
    }
    return undefined;
}

export function createDefaultConfig(dataWinPath: string): UtmtGitConfig {
    return {
        dataWinPath,
        assetDirs: { ...DEFAULT_ASSET_DIRS },
    };
}

export function resolveAssetDir(projectRoot: string, assetDirs: AssetDirs, type: keyof AssetDirs): string {
    return path.resolve(projectRoot, assetDirs[type]);
}

export async function listFiles(dirPath: string, pattern: string): Promise<string[]> {
    if (!await dirExists(dirPath)) {
        return [];
    }
    const entries = await fs.promises.readdir(dirPath, { withFileTypes: true });
    return entries
        .filter(e => e.isFile() && e.name.match(pattern))
        .map(e => path.join(dirPath, e.name));
}

export async function listSubdirs(dirPath: string): Promise<string[]> {
    if (!await dirExists(dirPath)) {
        return [];
    }
    const entries = await fs.promises.readdir(dirPath, { withFileTypes: true });
    return entries
        .filter(e => e.isDirectory())
        .map(e => path.join(dirPath, e.name));
}

export async function listAllFiles(dirPath: string, ext: string): Promise<string[]> {
    if (!await dirExists(dirPath)) {
        return [];
    }
    const result: string[] = [];
    const regex = new RegExp(`\\.${ext}$`, 'i');

    async function walk(dir: string): Promise<void> {
        const entries = await fs.promises.readdir(dir, { withFileTypes: true });
        for (const entry of entries) {
            const fullPath = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                await walk(fullPath);
            } else if (entry.isFile() && regex.test(entry.name)) {
                result.push(fullPath);
            }
        }
    }

    await walk(dirPath);
    return result;
}
