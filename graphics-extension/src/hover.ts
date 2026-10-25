import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { DesignerClient } from './server';
import { kindOf } from './shared/assets';
import { parseJson } from './shared/json';
import { GeneratedSource, GeneratedSymbol, assetOfIncluded, describeSymbol, parseGenerated, parseIncludeLine, renderItem } from './shared/labels';

/** The language id of WinASM65 sources (the WinASM65 extension's). */
export const ASM_LANGUAGE = 'winasm65';

/** The generated sources hovers know: what `AssetExporter` writes. */
const GENERATED_GLOB = '**/*.{pal,msprite,screen,map}.asm';

/** Largest side, in pixels, of a picture in a hover. */
const HOVER_SIZE = 256;

export interface IndexedSymbol {
  symbol: GeneratedSymbol;
  /** The generated source that defines it. */
  file: string;
  /** The asset it was made from. */
  asset: string;
}

/**
 * The labels and constants of the workspace's generated sources, by name
 * (case-insensitive, as the assembler compares them), kept up to date by a
 * file watcher.
 */
export class LabelIndex implements vscode.Disposable {
  private readonly byFile = new Map<string, GeneratedSource & { file: string }>();
  private readonly byName = new Map<string, IndexedSymbol[]>();
  private readonly watcher: vscode.FileSystemWatcher;
  private scanned: Promise<void> | undefined;

  constructor() {
    this.watcher = vscode.workspace.createFileSystemWatcher(GENERATED_GLOB);
    this.watcher.onDidCreate(uri => this.read(uri.fsPath));
    this.watcher.onDidChange(uri => this.read(uri.fsPath));
    this.watcher.onDidDelete(uri => this.remove(uri.fsPath));
  }

  /** Reads every generated source of the workspace (once; again when `force`). */
  scan(force = false): Promise<void> {
    if (!this.scanned || force) {
      this.scanned = (async () => {
        const files = await vscode.workspace.findFiles(GENERATED_GLOB, '**/node_modules/**', 5000);
        for (const file of files) {
          this.read(file.fsPath);
        }
      })();
    }
    return this.scanned;
  }

  /** Reads (or reads again) one generated source. */
  read(file: string): void {
    this.remove(file);
    let text: string;
    try {
      text = fs.readFileSync(file, 'utf8');
    } catch {
      return;
    }
    const parsed = parseGenerated(text);
    if (!parsed) {
      return;
    }
    const key = file.toLowerCase();
    this.byFile.set(key, { ...parsed, file });
    const asset = path.join(path.dirname(file), parsed.source);
    for (const symbol of parsed.symbols) {
      const name = symbol.name.toLowerCase();
      const list = this.byName.get(name) ?? [];
      list.push({ symbol, file, asset });
      this.byName.set(name, list);
    }
  }

  private remove(file: string): void {
    const key = file.toLowerCase();
    const old = this.byFile.get(key);
    if (!old) {
      return;
    }
    this.byFile.delete(key);
    for (const symbol of old.symbols) {
      const name = symbol.name.toLowerCase();
      const list = (this.byName.get(name) ?? []).filter(s => s.file.toLowerCase() !== key);
      if (list.length > 0) this.byName.set(name, list);
      else this.byName.delete(name);
    }
  }

  /** The symbols of a name; the one defined nearest to `near` (a source file) first. */
  lookup(name: string, near?: string): IndexedSymbol[] {
    const list = this.byName.get(name.toLowerCase()) ?? [];
    if (!near || list.length < 2) {
      return list;
    }
    const distance = (file: string) => path.relative(path.dirname(near), file).split(/[\\/]/).length;
    return list.slice().sort((a, b) => distance(a.file) - distance(b.file));
  }

  /** The parsed generated source of a file, if it is one. */
  generated(file: string): (GeneratedSource & { file: string }) | undefined {
    let found = this.byFile.get(file.toLowerCase());
    if (!found && fs.existsSync(file)) {
      this.read(file);
      found = this.byFile.get(file.toLowerCase());
    }
    return found;
  }

  dispose(): void {
    this.watcher.dispose();
  }
}

interface RenderReply {
  png: string;
  width: number;
  height: number;
  originX: number;
  originY: number;
}

/** A picture for a hover: the PNG at a whole scale, and its size at that scale. */
export interface HoverPicture {
  png: string;
  width: number;
  height: number;
  scale: number;
}

/**
 * Pictures of assets from the designer's `render`, enlarged by the largest
 * whole factor that keeps them within {@link HOVER_SIZE}, cached by asset,
 * item and the modification times of the asset and the files it names.
 */
export class RenderCache {
  private readonly cache = new Map<string, Promise<HoverPicture>>();

  constructor(private readonly client: DesignerClient) {}

  render(asset: string, item: string | undefined): Promise<HoverPicture> {
    const key = asset.toLowerCase() + '|' + (item ?? '') + '|' + stamps(asset);
    let found = this.cache.get(key);
    if (!found) {
      found = (async () => {
        const one = await this.client.request<RenderReply>('render', { path: asset, item, scale: 1 });
        const scale = Math.max(1, Math.min(8, Math.floor(HOVER_SIZE / Math.max(one.width, one.height, 1))));
        const reply = scale === 1 ? one : await this.client.request<RenderReply>('render', { path: asset, item, scale });
        return { png: reply.png, width: reply.width, height: reply.height, scale };
      })();
      found.catch(() => this.cache.delete(key));
      if (this.cache.size > 200) {
        this.cache.clear();
      }
      this.cache.set(key, found);
    }
    return found;
  }
}

/** Modification times of an asset and of the tile set and palette file it names. */
function stamps(asset: string): string {
  const mtime = (file: string) => {
    try {
      return String(fs.statSync(file).mtimeMs);
    } catch {
      return '-';
    }
  };
  const parts = [mtime(asset)];
  if (asset.toLowerCase().endsWith('.json')) {
    try {
      const refs = parseJson(fs.readFileSync(asset, 'utf8')) as { chr?: unknown; palettes?: unknown };
      for (const ref of [refs.chr, refs.palettes]) {
        if (typeof ref === 'string' && ref) parts.push(mtime(path.resolve(path.dirname(asset), ref)));
      }
    } catch {
      // drawn as the designer can
    }
  }
  return parts.join(',');
}

/**
 * Pictures in assembly sources: hovering a label or constant an asset export
 * defines shows what it was made from (the metasprite, the animation's first
 * frame, the palettes, the screen, the map), and so does hovering an
 * `.incbin` or `.include` of an asset's files.
 */
export class AssetHoverProvider implements vscode.HoverProvider {
  constructor(readonly index: LabelIndex, private readonly renders: RenderCache, private readonly log: vscode.OutputChannel) {}

  static register(client: DesignerClient, log: vscode.OutputChannel): { provider: AssetHoverProvider; disposables: vscode.Disposable[] } {
    const index = new LabelIndex();
    const provider = new AssetHoverProvider(index, new RenderCache(client), log);
    const registration = vscode.languages.registerHoverProvider({ language: ASM_LANGUAGE }, provider);
    void index.scan();
    return { provider, disposables: [index, registration] };
  }

  async provideHover(document: vscode.TextDocument, position: vscode.Position, token: vscode.CancellationToken): Promise<vscode.Hover | undefined> {
    if (!vscode.workspace.getConfiguration('winasm65Graphics').get<boolean>('hoverPictures', true)) {
      return undefined;
    }
    const line = document.lineAt(position.line).text;
    const include = parseIncludeLine(line);
    if (include && position.character >= include.start && position.character <= include.end) {
      const range = new vscode.Range(position.line, include.start, position.line, include.end);
      return this.includeHover(document, include.file, range, token);
    }

    const range = document.getWordRangeAtPosition(position, /[A-Za-z_@][\w@]*/);
    if (!range) {
      return undefined;
    }
    await this.index.scan();
    const word = document.getText(range);
    const found = this.index.lookup(word, document.uri.fsPath)[0];
    if (!found || token.isCancellationRequested) {
      return undefined;
    }
    let asset: unknown;
    try {
      asset = kindOf(found.asset) === 'metasprite' ? parseJson(fs.readFileSync(found.asset, 'utf8')) : undefined;
    } catch {
      asset = undefined;
    }
    const item = renderItem(found.symbol, asset);
    const what = describeSymbol(found.symbol, item);
    return this.pictureHover(found.asset, item, range,
      '`' + found.symbol.name + '`: ' + what + ' of ' + link(found.asset) + ' (generated in ' + link(found.file) + ')');
  }

  private async includeHover(document: vscode.TextDocument, file: string, range: vscode.Range, token: vscode.CancellationToken): Promise<vscode.Hover | undefined> {
    const target = await resolveIncluded(document, file);
    if (!target || token.isCancellationRequested) {
      return undefined;
    }
    let asset = assetOfIncluded(target);
    let what = path.basename(target);
    if (!asset && target.toLowerCase().endsWith('.asm')) {
      const generated = this.index.generated(target);
      if (!generated) {
        return undefined;
      }
      asset = path.join(path.dirname(target), generated.source);
      what = path.basename(target) + ', generated from ' + generated.source;
    }
    if (!asset || !fs.existsSync(asset)) {
      return undefined;
    }
    return this.pictureHover(asset, undefined, range, what + ': ' + link(asset));
  }

  private async pictureHover(asset: string, item: string | undefined, range: vscode.Range, caption: string): Promise<vscode.Hover | undefined> {
    let picture: HoverPicture;
    try {
      picture = await this.renders.render(asset, item);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.log.appendLine('hover: render ' + asset + (item ? ' ' + item : '') + ': ' + message);
      const text = new vscode.MarkdownString(caption + '\n\n*Cannot draw it: ' + escapeMarkdown(message) + '*');
      return new vscode.Hover(text, range);
    }
    const size = (picture.width / picture.scale) + '×' + (picture.height / picture.scale) + ' px' + (picture.scale > 1 ? ', shown ×' + picture.scale : '');
    const markdown = new vscode.MarkdownString(undefined, true);
    markdown.supportHtml = true;
    const origin = kindOf(asset) === 'metasprite' ? ' · origin in magenta' : '';
    markdown.appendMarkdown(caption + ' · ' + size + origin + '\n\n');
    markdown.appendMarkdown('<img src="data:image/png;base64,' + picture.png + '" width="' + picture.width + '" height="' + picture.height + '">');
    return new vscode.Hover(markdown, range);
  }
}

/** A file an `.incbin`/`.include` names: beside the source, else in a workspace folder, else anywhere by name. */
async function resolveIncluded(document: vscode.TextDocument, file: string): Promise<string | undefined> {
  const candidates: string[] = [];
  if (path.isAbsolute(file)) {
    candidates.push(file);
  } else {
    if (document.uri.scheme === 'file') candidates.push(path.resolve(path.dirname(document.uri.fsPath), file));
    for (const folder of vscode.workspace.workspaceFolders ?? []) candidates.push(path.resolve(folder.uri.fsPath, file));
  }
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  const found = await vscode.workspace.findFiles('**/' + path.basename(file), '**/node_modules/**', 2);
  return found.length === 1 ? found[0].fsPath : undefined;
}

function link(file: string): string {
  return '[' + escapeMarkdown(path.basename(file)) + '](' + vscode.Uri.file(file).toString() + ')';
}

function escapeMarkdown(text: string): string {
  return text.replace(/[\\`*_{}[\]()#+!|<>]/g, c => '\\' + c);
}
