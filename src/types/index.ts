export interface UtmtGitConfig {
    dataWinPath: string;
    assetDirs: AssetDirs;
}

export interface AssetDirs {
    code: string;
    sprites: string;
    sounds: string;
    objects: string;
    backgrounds: string;
    fonts: string;
    rooms: string;
}

export type AssetType = 'code' | 'sprites' | 'sounds' | 'objects' | 'backgrounds' | 'fonts' | 'rooms';

export interface AssetEntry {
    type: AssetType;
    name: string;
    path: string;
    isDirectory: boolean;
}

export const ASSET_TYPE_LABELS: Record<AssetType, string> = {
    code: '代码',
    sprites: '精灵图',
    sounds: '音效',
    objects: '对象',
    backgrounds: '背景',
    fonts: '字体',
    rooms: '房间',
};

export const ASSET_TYPE_ICONS: Record<AssetType, string> = {
    code: 'file-code',
    sprites: 'file-media',
    sounds: 'file-media',
    objects: 'file-code',
    backgrounds: 'file-media',
    fonts: 'file-media',
    rooms: 'symbol-structure',
};

export const DEFAULT_ASSET_DIRS: AssetDirs = {
    code: 'code',
    sprites: 'sprites',
    sounds: 'sounds',
    objects: 'objects',
    backgrounds: 'backgrounds',
    fonts: 'fonts',
    rooms: 'rooms',
};

export const CONFIG_FILE_NAME = '.utmt-git.json';

export const CLI_ENV_PROJECT_ROOT = 'UTMT_PROJECT_ROOT';
