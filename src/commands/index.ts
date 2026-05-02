import * as vscode from 'vscode';
import { registerInitProject } from './initProject';
import { registerImportAssets } from './importAssets';
import { registerExportAssets } from './exportAssets';
import { registerOpenAsset } from './openAsset';
import { registerAssetOperations } from './assetOperations';
import { ProjectService } from '../services/projectService';
import { CliService } from '../services/cliService';
import { Logger } from '../utils/logger';

export function registerAllCommands(
    context: vscode.ExtensionContext,
    projectService: ProjectService,
    cliService: CliService,
    logger: Logger
): void {
    registerInitProject(context, projectService, logger);
    registerImportAssets(context, projectService, cliService, logger);
    registerExportAssets(context, projectService, cliService, logger);
    registerOpenAsset(context);
    registerAssetOperations(context, projectService, logger);
}
