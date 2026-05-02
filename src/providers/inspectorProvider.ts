import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { ProjectService } from '../services/projectService';
import { AssetTreeItem } from './assetTreeProvider';
import { AssetType } from '../types';

type InspectorItem = InspectorSection | InspectorProperty | InspectorLink | InspectorEventItem | InspectorNavigateLink;

class InspectorSection extends vscode.TreeItem {
    constructor(label: string, children: InspectorItem[] = []) {
        super(label, vscode.TreeItemCollapsibleState.Expanded);
        this.contextValue = 'inspector-section';
        this.children = children;
    }
    children: InspectorItem[];
}

class InspectorProperty extends vscode.TreeItem {
    constructor(label: string, value: string) {
        super(`${label}: ${value}`, vscode.TreeItemCollapsibleState.None);
        this.contextValue = 'inspector-property';
        this.description = value;
    }
}

class InspectorLink extends vscode.TreeItem {
    constructor(label: string, public readonly targetPath: string) {
        super(label, vscode.TreeItemCollapsibleState.None);
        this.contextValue = 'inspector-link';
        this.iconPath = new vscode.ThemeIcon('link');
        this.command = {
            command: 'vscode.open',
            title: '打开',
            arguments: [vscode.Uri.file(targetPath)],
        };
    }
}

class InspectorNavigateLink extends vscode.TreeItem {
    constructor(label: string, public readonly assetFilePath: string) {
        super(label, vscode.TreeItemCollapsibleState.None);
        this.contextValue = 'inspector-navigate';
        this.iconPath = new vscode.ThemeIcon('go-to-file');
        this.command = {
            command: 'vscode-utmt-git.navigateToAsset',
            title: '定位到资产',
            arguments: [assetFilePath],
        };
        this.tooltip = `点击定位到资产树并显示详情：${path.basename(assetFilePath)}`;
    }
}

class InspectorEventItem extends vscode.TreeItem {
    constructor(
        label: string,
        eventType: string,
        eventSubtype: number,
        codeName: string | null,
        codeFilePath: string | null
    ) {
        const collapsible = codeFilePath ? vscode.TreeItemCollapsibleState.None : vscode.TreeItemCollapsibleState.None;
        super(label, collapsible);
        this.contextValue = codeFilePath ? 'inspector-event-with-code' : 'inspector-event';
        this.eventType = eventType;
        this.eventSubtype = eventSubtype;
        this.codeName = codeName;
        this.codeFilePath = codeFilePath;

        if (codeFilePath) {
            this.iconPath = new vscode.ThemeIcon('file-code');
            this.description = codeName || '';
            this.command = {
                command: 'vscode.open',
                title: '打开代码',
                arguments: [vscode.Uri.file(codeFilePath)],
            };
            this.tooltip = `点击打开：${codeName}.gml`;
        } else {
            this.iconPath = new vscode.ThemeIcon('circle-outline');
            this.description = '无关联代码';
            this.tooltip = `${eventType} (子类型 ${eventSubtype}) — 无关联代码`;
        }
    }

    readonly eventType: string;
    readonly eventSubtype: number;
    readonly codeName: string | null;
    readonly codeFilePath: string | null;
}

const EVENT_TYPE_NAMES: Record<string, string> = {
    Create: '创建',
    Destroy: '销毁',
    Step: '步',
    Alarm: '闹钟',
    Keyboard: '键盘',
    Mouse: '鼠标',
    Collision: '碰撞',
    Other: '其他',
    Draw: '绘制',
    KeyPress: '按键按下',
    KeyRelease: '按键释放',
    Trigger: '触发器',
    Gesture: '手势',
    Async: '异步',
    CleanUp: '清理',
    PreCreate: '预创建',
};

const STEP_SUBTYPE_NAMES: Record<number, string> = {
    0: '正常',
    1: '开始',
    2: '结束',
};

const DRAW_SUBTYPE_NAMES: Record<number, string> = {
    0: '正常',
    64: 'GUI',
    65: 'GUI 开始',
    66: 'GUI 结束',
    67: '窗口大小改变',
    73: '动画更新',
    74: '动画事件',
};

function getEventDisplayName(eventType: string, eventSubtype: number): string {
    const typeName = EVENT_TYPE_NAMES[eventType] || eventType;

    if (eventType === 'Step') {
        const subName = STEP_SUBTYPE_NAMES[eventSubtype];
        return subName ? `${typeName}_${subName}` : `${typeName}_${eventSubtype}`;
    }

    if (eventType === 'Draw') {
        const subName = DRAW_SUBTYPE_NAMES[eventSubtype];
        return subName ? `${typeName}_${subName}` : `${typeName}_${eventSubtype}`;
    }

    if (eventType === 'Alarm') {
        return `${typeName}_${eventSubtype}`;
    }

    if (eventType === 'Collision') {
        return `${typeName}`;
    }

    if (eventType === 'Create' || eventType === 'Destroy' || eventType === 'CleanUp' || eventType === 'PreCreate') {
        return typeName;
    }

    return `${typeName}_${eventSubtype}`;
}

function getEventIcon(eventType: string): vscode.ThemeIcon {
    switch (eventType) {
        case 'Create': return new vscode.ThemeIcon('add');
        case 'Destroy': return new vscode.ThemeIcon('trash');
        case 'Step': return new vscode.ThemeIcon('play');
        case 'Draw': return new vscode.ThemeIcon('eye');
        case 'Alarm': return new vscode.ThemeIcon('clock');
        case 'Collision': return new vscode.ThemeIcon('debug-breakpoint');
        case 'Keyboard':
        case 'KeyPress':
        case 'KeyRelease': return new vscode.ThemeIcon('keyboard');
        case 'Mouse': return new vscode.ThemeIcon('mouse');
        case 'Async': return new vscode.ThemeIcon('sync');
        case 'Gesture': return new vscode.ThemeIcon('symbol-event');
        default: return new vscode.ThemeIcon('symbol-event');
    }
}

export class InspectorProvider implements vscode.TreeDataProvider<InspectorItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<InspectorItem | undefined | null>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private selectedItem: AssetTreeItem | undefined;

    constructor(private projectService: ProjectService) {}

    inspect(item: AssetTreeItem): void {
        this.selectedItem = item;
        this._onDidChangeTreeData.fire(undefined);
    }

    refresh(): void {
        this._onDidChangeTreeData.fire(undefined);
    }

    getTreeItem(element: InspectorItem): vscode.TreeItem {
        return element;
    }

    getChildren(element?: InspectorItem): InspectorItem[] {
        if (!element) {
            return this.getRootItems();
        }

        if (element instanceof InspectorSection) {
            return element.children;
        }

        return [];
    }

    private getRootItems(): InspectorItem[] {
        if (!this.selectedItem) {
            return [new InspectorProperty('未选中资产', '')];
        }

        const item = this.selectedItem;

        if (item.contextValue === 'asset-object-json' && item.filePath) {
            return this.getObjectInspector(item.filePath);
        }

        if ((item.contextValue === 'asset-sprite-dir') && item.filePath) {
            return this.getSpriteInspector(item.filePath);
        }

        if ((item.contextValue === 'asset-sound-dir') && item.filePath) {
            return this.getSoundInspector(item.filePath);
        }

        if (item.contextValue === 'asset-file-gml' && item.filePath) {
            return this.getCodeInspector(item.filePath);
        }

        if (item.filePath) {
            return this.getGenericInspector(item.filePath, item.contextValue);
        }

        return [new InspectorProperty('无可用详情', '')];
    }

    private resolveCodeFile(codeName: string | null): string | null {
        if (!codeName) { return null; }

        const codeDir = this.projectService.getAssetDir('code');
        if (!codeDir) { return null; }

        const directPath = path.join(codeDir, `${codeName}.gml`);
        if (fs.existsSync(directPath)) {
            return directPath;
        }

        const lowerPath = path.join(codeDir, codeName, `${codeName}.gml`);
        if (fs.existsSync(lowerPath)) {
            return lowerPath;
        }

        try {
            const searchInDir = (dir: string): string | null => {
                if (!fs.existsSync(dir)) { return null; }
                const entries = fs.readdirSync(dir, { withFileTypes: true });
                for (const entry of entries) {
                    if (entry.name === `${codeName}.gml`) {
                        return path.join(dir, entry.name);
                    }
                    if (entry.isDirectory() && entry.name !== '.gitkeep') {
                        const found = searchInDir(path.join(dir, entry.name));
                        if (found) { return found; }
                    }
                }
                return null;
            };

            return searchInDir(codeDir);
        } catch {
            return null;
        }
    }

    private getObjectInspector(filePath: string): InspectorItem[] {
        const items: InspectorItem[] = [];

        try {
            const content = fs.readFileSync(filePath, 'utf-8');
            const obj = JSON.parse(content);

            const basicProps: InspectorItem[] = [];
            if (obj.Name) { basicProps.push(new InspectorProperty('名称', String(obj.Name))); }
            if (obj.Sprite !== undefined) {
                const spriteName = String(obj.Sprite);
                const spriteDir = this.projectService.getAssetDir('sprites');
                const spritePath = spriteDir ? path.join(spriteDir, spriteName) : null;
                if (spritePath && fs.existsSync(spritePath)) {
                    const link = new InspectorNavigateLink(`精灵: ${spriteName}`, spritePath);
                    link.iconPath = new vscode.ThemeIcon('symbol-color');
                    basicProps.push(link);
                } else {
                    basicProps.push(new InspectorProperty('精灵', spriteName));
                }
            }
            if (obj.Visible !== undefined) { basicProps.push(new InspectorProperty('可见', String(obj.Visible))); }
            if (obj.Solid !== undefined) { basicProps.push(new InspectorProperty('实体', String(obj.Solid))); }
            if (obj.Depth !== undefined) { basicProps.push(new InspectorProperty('深度', String(obj.Depth))); }
            if (obj.Persistent !== undefined) { basicProps.push(new InspectorProperty('持久', String(obj.Persistent))); }
            if (obj.Parent !== undefined) {
                const parentName = String(obj.Parent);
                if (parentName && parentName !== 'noone' && parentName !== '-1') {
                    const objectsDir = this.projectService.getAssetDir('objects');
                    const parentJsonPath = objectsDir ? path.join(objectsDir, `${parentName}.json`) : null;
                    if (parentJsonPath && fs.existsSync(parentJsonPath)) {
                        const link = new InspectorNavigateLink(`父对象: ${parentName}`, parentJsonPath);
                        link.iconPath = new vscode.ThemeIcon('symbol-class');
                        basicProps.push(link);
                    } else {
                        basicProps.push(new InspectorProperty('父对象', parentName));
                    }
                }
            }
            items.push(new InspectorSection('基本属性', basicProps));

            if (obj.Events && Array.isArray(obj.Events) && obj.Events.length > 0) {
                const eventItems: InspectorItem[] = obj.Events.map((evt: any) => {
                    const eventType = evt.EventType || 'Unknown';
                    const eventSubtype = typeof evt.EventSubtype === 'number' ? evt.EventSubtype : 0;
                    const codeName = evt.CodeName || null;
                    const codeFilePath = this.resolveCodeFile(codeName);
                    const displayName = getEventDisplayName(eventType, eventSubtype);
                    const icon = getEventIcon(eventType);

                    const item = new InspectorEventItem(displayName, eventType, eventSubtype, codeName, codeFilePath);
                    if (!codeFilePath) {
                        item.iconPath = icon;
                    }
                    return item;
                });
                items.push(new InspectorSection(`事件 (${obj.Events.length})`, eventItems));
            }

            if (obj.UsesPhysics !== undefined) {
                const physProps: InspectorItem[] = [];
                if (obj.UsesPhysics !== undefined) { physProps.push(new InspectorProperty('启用物理', String(obj.UsesPhysics))); }
                if (obj.IsSensor !== undefined) { physProps.push(new InspectorProperty('传感器', String(obj.IsSensor))); }
                if (obj.CollisionShape !== undefined) { physProps.push(new InspectorProperty('碰撞形状', String(obj.CollisionShape))); }
                if (obj.Density !== undefined) { physProps.push(new InspectorProperty('密度', String(obj.Density))); }
                if (obj.Restitution !== undefined) { physProps.push(new InspectorProperty('弹性', String(obj.Restitution))); }
                items.push(new InspectorSection('物理属性', physProps));
            }
        } catch {
            items.push(new InspectorProperty('错误', '解析对象 JSON 失败'));
        }

        return items;
    }

    private getSpriteInspector(dirPath: string): InspectorItem[] {
        const items: InspectorItem[] = [];

        try {
            const metadataPath = path.join(dirPath, 'metadata.json');

            if (fs.existsSync(metadataPath)) {
                const content = fs.readFileSync(metadataPath, 'utf-8');
                const meta = JSON.parse(content);

                const props: InspectorItem[] = [];
                if (meta.Width !== undefined) { props.push(new InspectorProperty('宽度', String(meta.Width))); }
                if (meta.Height !== undefined) { props.push(new InspectorProperty('高度', String(meta.Height))); }
                if (meta.XOrigin !== undefined) { props.push(new InspectorProperty('原点X', String(meta.XOrigin))); }
                if (meta.YOrigin !== undefined) { props.push(new InspectorProperty('原点Y', String(meta.YOrigin))); }
                if (meta.BBoxMode !== undefined) { props.push(new InspectorProperty('包围盒模式', String(meta.BBoxMode))); }
                if (meta.PlaybackSpeed !== undefined) { props.push(new InspectorProperty('播放速度', String(meta.PlaybackSpeed))); }
                items.push(new InspectorSection('精灵属性', props));
            }

            const pngFiles = fs.readdirSync(dirPath).filter(f => f.endsWith('.png'));
            if (pngFiles.length > 0) {
                const frameItems = pngFiles.map(f => new InspectorLink(f, path.join(dirPath, f)));
                items.push(new InspectorSection(`帧 (${pngFiles.length})`, frameItems));
            }
        } catch {
            items.push(new InspectorProperty('错误', '读取精灵信息失败'));
        }

        return items;
    }

    private getSoundInspector(dirPath: string): InspectorItem[] {
        const items: InspectorItem[] = [];

        try {
            const metadataPath = path.join(dirPath, 'metadata.json');

            if (fs.existsSync(metadataPath)) {
                const content = fs.readFileSync(metadataPath, 'utf-8');
                const meta = JSON.parse(content);

                const props: InspectorItem[] = [];
                if (meta.Type !== undefined) { props.push(new InspectorProperty('类型', String(meta.Type))); }
                if (meta.Volume !== undefined) { props.push(new InspectorProperty('音量', String(meta.Volume))); }
                if (meta.Pitch !== undefined) { props.push(new InspectorProperty('音调', String(meta.Pitch))); }
                if (meta.AudioGroup !== undefined) { props.push(new InspectorProperty('音频组', String(meta.AudioGroup))); }
                if (meta.Flags !== undefined) { props.push(new InspectorProperty('标志', String(meta.Flags))); }
                items.push(new InspectorSection('音效属性', props));
            }

            const audioFiles = fs.readdirSync(dirPath).filter(f => f.endsWith('.ogg') || f.endsWith('.wav') || f.endsWith('.mp3'));
            if (audioFiles.length > 0) {
                const fileItems = audioFiles.map(f => new InspectorLink(f, path.join(dirPath, f)));
                items.push(new InspectorSection('音频文件', fileItems));
            }
        } catch {
            items.push(new InspectorProperty('错误', '读取音效信息失败'));
        }

        return items;
    }

    private getCodeInspector(filePath: string): InspectorItem[] {
        const items: InspectorItem[] = [];

        try {
            const stat = fs.statSync(filePath);
            items.push(new InspectorSection('文件信息', [
                new InspectorProperty('路径', filePath),
                new InspectorProperty('大小', `${stat.size} 字节`),
                new InspectorProperty('修改时间', stat.mtime.toLocaleString()),
            ]));
        } catch {
            items.push(new InspectorProperty('错误', '读取文件信息失败'));
        }

        return items;
    }

    private getGenericInspector(filePath: string, contextValue?: string): InspectorItem[] {
        const items: InspectorItem[] = [];

        try {
            const stat = fs.statSync(filePath);
            const ext = path.extname(filePath).toLowerCase();
            items.push(new InspectorSection('文件信息', [
                new InspectorProperty('名称', path.basename(filePath)),
                new InspectorProperty('类型', ext || '未知'),
                new InspectorProperty('大小', `${stat.size} 字节`),
                new InspectorProperty('修改时间', stat.mtime.toLocaleString()),
            ]));
        } catch {
            items.push(new InspectorProperty('错误', '读取文件信息失败'));
        }

        return items;
    }
}
