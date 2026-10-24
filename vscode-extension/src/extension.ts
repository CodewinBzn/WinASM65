import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions } from 'vscode-languageclient/node';

const DEBUG_TYPE = 'winasm65';

let client: LanguageClient | undefined;

/**
 * How to start WinASM65.Tooling: an explicit setting, the copy shipped in the
 * extension, or, when the extension runs from the repository, the debug build
 * next to it.
 */
export function resolveTooling(context: vscode.ExtensionContext): { command: string; args: string[] } | undefined {
  const configured = vscode.workspace.getConfiguration('winasm65').get<string>('toolingPath')?.trim();
  const candidates: string[] = [];
  if (configured) {
    candidates.push(configured);
  }
  candidates.push(path.join(context.extensionPath, 'bin', 'WinASM65.Tooling.exe'));
  candidates.push(path.join(context.extensionPath, 'bin', 'WinASM65.Tooling'));
  candidates.push(path.join(context.extensionPath, '..', 'WinASM65.Tooling', 'bin', 'Debug', 'net8.0', 'WinASM65.Tooling.dll'));

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate.toLowerCase().endsWith('.dll')
        ? { command: 'dotnet', args: [candidate] }
        : { command: candidate, args: [] };
    }
  }
  return undefined;
}

class AdapterFactory implements vscode.DebugAdapterDescriptorFactory {
  constructor(private readonly context: vscode.ExtensionContext) {}

  createDebugAdapterDescriptor(): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
    const tooling = resolveTooling(this.context);
    if (!tooling) {
      throw new Error('WinASM65.Tooling was not found. Set "winasm65.toolingPath".');
    }
    return new vscode.DebugAdapterExecutable(tooling.command, [...tooling.args, '--dap']);
  }
}

class ConfigurationProvider implements vscode.DebugConfigurationProvider {
  /**
   * F5 with no launch.json: debug the project whose config.json is in the
   * workspace, the way the terminal monitor's F5 builds it.
   */
  async resolveDebugConfiguration(
    folder: vscode.WorkspaceFolder | undefined,
    config: vscode.DebugConfiguration
  ): Promise<vscode.DebugConfiguration | undefined> {
    if (!config.type && !config.request && !config.name) {
      const project = await findProject(folder);
      if (!project) {
        await vscode.window.showErrorMessage('No config.json found: a WinASM65 project is needed to debug.');
        return undefined;
      }
      config.type = DEBUG_TYPE;
      config.request = 'launch';
      config.name = 'Debug on Mesen2';
      config.config = project;
    }

    if (config.request === 'launch' && !config.emulatorPath) {
      const emulator = vscode.workspace.getConfiguration('winasm65').get<string>('emulatorPath')?.trim();
      if (emulator) {
        config.emulatorPath = emulator;
      }
    }
    return config;
  }
}

async function findProject(folder: vscode.WorkspaceFolder | undefined): Promise<string | undefined> {
  const active = vscode.window.activeTextEditor?.document.uri.fsPath;
  if (active) {
    // The config.json closest to the open source governs it.
    let dir = path.dirname(active);
    for (let i = 0; i < 8; i++) {
      const candidate = path.join(dir, 'config.json');
      if (fs.existsSync(candidate)) {
        return candidate;
      }
      const parent = path.dirname(dir);
      if (parent === dir) {
        break;
      }
      dir = parent;
    }
  }
  const pattern = folder ? new vscode.RelativePattern(folder, '**/config.json') : '**/config.json';
  const found = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 2);
  return found.length > 0 ? found[0].fsPath : undefined;
}

interface BuildTaskDefinition extends vscode.TaskDefinition {
  config: string;
}

/**
 * "WinASM65: build" for every project in the workspace. The build runs
 * WinASM65.Tooling --build, which prints file:line: severity: message lines the
 * $winasm65 problem matcher turns into the Problems panel.
 */
class BuildTaskProvider implements vscode.TaskProvider {
  constructor(private readonly context: vscode.ExtensionContext) {}

  async provideTasks(): Promise<vscode.Task[]> {
    const projects = await vscode.workspace.findFiles('**/config.json', '**/node_modules/**', 20);
    const tasks: vscode.Task[] = [];
    for (const project of projects) {
      const folder = vscode.workspace.getWorkspaceFolder(project);
      const task = this.build({ type: DEBUG_TYPE, config: project.fsPath }, folder ?? vscode.TaskScope.Workspace,
        'build ' + vscode.workspace.asRelativePath(project));
      if (task) {
        tasks.push(task);
      }
    }
    return tasks;
  }

  resolveTask(task: vscode.Task): vscode.Task | undefined {
    const definition = task.definition as BuildTaskDefinition;
    if (!definition.config) {
      return undefined;
    }
    return this.build(definition, task.scope ?? vscode.TaskScope.Workspace, task.name);
  }

  private build(definition: BuildTaskDefinition, scope: vscode.WorkspaceFolder | vscode.TaskScope, name: string): vscode.Task | undefined {
    const tooling = resolveTooling(this.context);
    if (!tooling) {
      return undefined;
    }
    let config = definition.config;
    if (typeof scope === 'object' && !path.isAbsolute(config)) {
      config = path.join(scope.uri.fsPath, config.replace('${workspaceFolder}', ''));
    }
    config = config.replace('${workspaceFolder}', typeof scope === 'object' ? scope.uri.fsPath : '');

    const task = new vscode.Task(definition, scope, name, 'winasm65',
      new vscode.ProcessExecution(tooling.command, [...tooling.args, '--build', config], { cwd: path.dirname(config) }),
      '$winasm65');
    task.group = vscode.TaskGroup.Build;
    return task;
  }
}

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory(DEBUG_TYPE, new AdapterFactory(context)),
    vscode.debug.registerDebugConfigurationProvider(DEBUG_TYPE, new ConfigurationProvider()),
    vscode.tasks.registerTaskProvider(DEBUG_TYPE, new BuildTaskProvider(context))
  );
  startLanguageServer(context);
}

/** Diagnostics as you type and hover, from WinASM65.Tooling --lsp. */
function startLanguageServer(context: vscode.ExtensionContext): void {
  if (!vscode.workspace.getConfiguration('winasm65').get<boolean>('languageServer.enabled', true)) {
    return;
  }
  const tooling = resolveTooling(context);
  if (!tooling) {
    void vscode.window.showWarningMessage('WinASM65.Tooling was not found: no diagnostics. Set "winasm65.toolingPath".');
    return;
  }
  const server: ServerOptions = { command: tooling.command, args: [...tooling.args, '--lsp'] };
  const options: LanguageClientOptions = {
    documentSelector: [{ scheme: 'file', language: 'winasm65' }],
    synchronize: { fileEvents: vscode.workspace.createFileSystemWatcher('**/config.json') }
  };
  client = new LanguageClient('winasm65', 'WinASM65', server, options);
  void client.start();
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
