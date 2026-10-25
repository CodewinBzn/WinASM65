import * as vscode from 'vscode';
import { registerCommands } from './commands';
import { Designer } from './designer';
import { EditorRegistry } from './editors/context';
import { JsonAssetEditorProvider, JsonEditorSpec } from './editors/jsonEditor';
import { TilesEditorProvider } from './editors/tilesEditor';
import { Exporter } from './exporting';
import { AssetResources } from './resources';
import { DesignerClient } from './server';

/** The JSON asset editors; part 2 adds the metasprite editor here. */
export const JSON_EDITORS: JsonEditorSpec[] = [
  { viewType: 'winasm65Graphics.palettes', kind: 'palette', script: 'palette', usesResources: false },
  { viewType: 'winasm65Graphics.screen', kind: 'screen', script: 'screen', usesResources: true },
  { viewType: 'winasm65Graphics.map', kind: 'map', script: 'map', usesResources: true }
];

/** What the extension returns from activate: for the tests, and for other extensions. */
export interface GraphicsApi {
  designer: Designer;
  registry: EditorRegistry;
  jsonEditors: Map<string, JsonAssetEditorProvider>;
  tiles: TilesEditorProvider;
  exporter: Exporter;
}

let client: DesignerClient | undefined;

export function activate(context: vscode.ExtensionContext): GraphicsApi {
  const log = vscode.window.createOutputChannel('WinASM65 Graphics');
  client = new DesignerClient(context.extensionPath, log);
  const designer = new Designer(client, log);
  const resources = new AssetResources();
  const registry = new EditorRegistry();
  const editorContext = { extensionUri: context.extensionUri, designer, resources, log, registry };
  const exporter = new Exporter(designer, log);
  context.subscriptions.push(log, client, resources, exporter);

  const jsonEditors = new Map<string, JsonAssetEditorProvider>();
  for (const spec of JSON_EDITORS) {
    const { provider, registration } = JsonAssetEditorProvider.register(editorContext, spec);
    jsonEditors.set(spec.viewType, provider);
    context.subscriptions.push(registration);
  }
  const tiles = TilesEditorProvider.register(editorContext, context.workspaceState);
  context.subscriptions.push(tiles.registration);
  context.subscriptions.push(...registerCommands(designer, exporter, registry));

  return { designer, registry, jsonEditors, tiles: tiles.provider, exporter };
}

export async function deactivate(): Promise<void> {
  await client?.shutdown();
}
