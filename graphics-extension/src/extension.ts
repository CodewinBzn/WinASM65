import * as vscode from 'vscode';
import { registerCommands } from './commands';
import { Designer } from './designer';
import { EditorRegistry } from './editors/context';
import { JsonAssetEditorProvider, JsonEditorSpec } from './editors/jsonEditor';
import { TilesEditorProvider } from './editors/tilesEditor';
import { Exporter } from './exporting';
import { AssetHoverProvider } from './hover';
import { PngImport } from './importing';
import { LivePreview } from './live';
import { AssetResources } from './resources';
import { DesignerClient } from './server';
import { PaletteAsset, ScreenAsset } from './shared/assets';
import { encodePalettes, encodeScreen } from './shared/binary';

/** The JSON asset editors. */
export const JSON_EDITORS: JsonEditorSpec[] = [
  {
    viewType: 'winasm65Graphics.palettes', kind: 'palette', script: 'palette', usesResources: false,
    live: {
      what: 'the palettes into palette RAM (PAL)',
      blocks: (asset, system) => [{ space: 'PAL', address: 0, data: encodePalettes(system, asset as PaletteAsset) }]
    }
  },
  {
    viewType: 'winasm65Graphics.screen', kind: 'screen', script: 'screen', usesResources: true, importMode: 'background',
    live: {
      what: 'the screen into the first nametable ($2000 of PPU memory)',
      blocks: (asset, system) => {
        const screen = asset as ScreenAsset;
        return [{ space: 'PPU', address: 0x2000, data: encodeScreen(system, screen.tiles ?? [], screen.attributes ?? []) }];
      }
    }
  },
  {
    viewType: 'winasm65Graphics.map', kind: 'map', script: 'map', usesResources: true,
    live: { unsupported: 'No live preview for maps: the game draws them from its tables. Export, then rebuild.' }
  },
  {
    viewType: 'winasm65Graphics.metasprites', kind: 'metasprite', script: 'metasprite', usesResources: true, importMode: 'sprites',
    live: { unsupported: 'No live preview for metasprites: the game rewrites the sprite table (OAM) every frame from its own tables, so a write would be gone at once. Export, then rebuild.' }
  }
];

/** What the extension returns from activate: for the tests, and for other extensions. */
export interface GraphicsApi {
  designer: Designer;
  registry: EditorRegistry;
  jsonEditors: Map<string, JsonAssetEditorProvider>;
  tiles: TilesEditorProvider;
  exporter: Exporter;
  live: LivePreview;
  hover: AssetHoverProvider;
  importer: PngImport;
}

let client: DesignerClient | undefined;

export function activate(context: vscode.ExtensionContext): GraphicsApi {
  const log = vscode.window.createOutputChannel('WinASM65 Graphics');
  client = new DesignerClient(context.extensionPath, log);
  const designer = new Designer(client, log);
  const resources = new AssetResources();
  const registry = new EditorRegistry();
  const live = new LivePreview(log);
  const editorContext = { extensionUri: context.extensionUri, designer, resources, log, registry, live };
  const exporter = new Exporter(designer, log);
  context.subscriptions.push(log, client, resources, exporter, live);

  const jsonEditors = new Map<string, JsonAssetEditorProvider>();
  for (const spec of JSON_EDITORS) {
    const { provider, registration } = JsonAssetEditorProvider.register(editorContext, spec);
    jsonEditors.set(spec.viewType, provider);
    context.subscriptions.push(registration);
  }
  const tiles = TilesEditorProvider.register(editorContext, context.workspaceState);
  context.subscriptions.push(tiles.registration);
  context.subscriptions.push(...registerCommands(designer, exporter, registry));
  const importer = new PngImport(client, log, tiles.provider);
  context.subscriptions.push(importer.register());
  const hover = AssetHoverProvider.register(client, log);
  context.subscriptions.push(...hover.disposables);

  return { designer, registry, jsonEditors, tiles: tiles.provider, exporter, live, hover: hover.provider, importer };
}

export async function deactivate(): Promise<void> {
  await client?.shutdown();
}
