import * as path from 'path';
import * as vscode from 'vscode';
import { PaletteAsset } from './shared/assets';
import { parseJson } from './shared/json';
import { ChrResource, PaletteResource } from './shared/protocol';

/**
 * The files assets refer to (tile sets, palette files), read as the user sees
 * them: an open, unsaved editor wins over the disk, so a screen follows the
 * tile being drawn in the next editor before it is saved.
 */
export class AssetResources implements vscode.Disposable {
  private readonly liveChr = new Map<string, () => Uint8Array>();
  private readonly emitter = new vscode.EventEmitter<vscode.Uri>();
  private readonly subscriptions: vscode.Disposable[] = [];

  /** A file that assets may refer to changed (on disk, in a text editor or in a tile editor). */
  readonly onDidChange = this.emitter.event;

  constructor() {
    this.subscriptions.push(
      this.emitter,
      vscode.workspace.onDidChangeTextDocument(event => {
        if (event.contentChanges.length > 0) {
          this.emitter.fire(event.document.uri);
        }
      })
    );
  }

  /** Lets an open tile editor provide the bytes of its file, unsaved edits included. */
  registerChr(uri: vscode.Uri, bytes: () => Uint8Array): vscode.Disposable {
    const key = uri.toString();
    this.liveChr.set(key, bytes);
    return new vscode.Disposable(() => {
      if (this.liveChr.get(key) === bytes) {
        this.liveChr.delete(key);
        this.emitter.fire(uri); // back to the file on disk
      }
    });
  }

  /** Tells the assets that refer to a file that it changed. */
  changed(uri: vscode.Uri): void {
    this.emitter.fire(uri);
  }

  async readChr(uri: vscode.Uri): Promise<Uint8Array> {
    const live = this.liveChr.get(uri.toString());
    return live ? live() : vscode.workspace.fs.readFile(uri);
  }

  async readText(uri: vscode.Uri): Promise<string> {
    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString());
    return open ? open.getText() : Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8');
  }

  async readPalette(uri: vscode.Uri): Promise<PaletteAsset> {
    const asset = parseJson(await this.readText(uri)) as PaletteAsset;
    if (!asset || !Array.isArray(asset.palettes)) {
      throw new Error('not a palette file');
    }
    return asset;
  }

  dispose(): void {
    this.subscriptions.forEach(s => s.dispose());
  }
}

/** A path an asset names, resolved against the asset's folder. */
export function resolveRelative(asset: vscode.Uri, relative: string): vscode.Uri {
  return path.isAbsolute(relative)
    ? vscode.Uri.file(relative)
    : vscode.Uri.joinPath(asset, '..', ...relative.split(/[\\/]/));
}

/** A path as an asset names it: relative to its folder, with forward slashes. */
export function relativePath(asset: vscode.Uri, target: vscode.Uri): string {
  return path.relative(path.dirname(asset.fsPath), target.fsPath).replace(/\\/g, '/');
}

/**
 * The tile set and the palette file one open asset names: read, sent to its
 * webview, and sent again whenever either changes or the asset names others.
 */
export class ResourceLink implements vscode.Disposable {
  private chr: string | undefined;
  private palettes: string | undefined;
  private watched: vscode.Disposable[] = [];
  private readonly subscription: vscode.Disposable;
  private timer: NodeJS.Timeout | undefined;

  constructor(
    private readonly resources: AssetResources,
    private readonly asset: vscode.Uri,
    private readonly send: (chr: ChrResource | undefined, palettes: PaletteResource | undefined) => void
  ) {
    this.subscription = resources.onDidChange(uri => {
      const changed = uri.toString();
      if ((this.chr && resolveRelative(asset, this.chr).toString() === changed)
        || (this.palettes && resolveRelative(asset, this.palettes).toString() === changed)) {
        this.schedule();
      }
    });
  }

  /** Follows the references of the asset as it now is; reads them when they differ (or when forced). */
  update(chr: string | undefined, palettes: string | undefined, force = false): void {
    if (!force && chr === this.chr && palettes === this.palettes) {
      return;
    }
    this.chr = chr || undefined;
    this.palettes = palettes || undefined;
    this.watched.forEach(w => w.dispose());
    this.watched = [];
    for (const relative of [this.chr, this.palettes]) {
      if (relative && this.asset.scheme === 'file') {
        const target = resolveRelative(this.asset, relative);
        const watcher = vscode.workspace.createFileSystemWatcher(
          new vscode.RelativePattern(vscode.Uri.file(path.dirname(target.fsPath)), path.basename(target.fsPath)));
        const fire = () => this.resources.changed(target);
        watcher.onDidChange(fire);
        watcher.onDidCreate(fire);
        watcher.onDidDelete(fire);
        this.watched.push(watcher);
      }
    }
    void this.read();
  }

  private schedule(): void {
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.timer = setTimeout(() => void this.read(), 50);
  }

  private async read(): Promise<void> {
    let chr: ChrResource | undefined;
    let palettes: PaletteResource | undefined;
    if (this.chr) {
      try {
        chr = { path: this.chr, data: Array.from(await this.resources.readChr(resolveRelative(this.asset, this.chr))) };
      } catch (error) {
        chr = { path: this.chr, error: 'tile set ' + this.chr + ' not found' };
      }
    }
    if (this.palettes) {
      try {
        palettes = { path: this.palettes, asset: await this.resources.readPalette(resolveRelative(this.asset, this.palettes)) };
      } catch (error) {
        palettes = { path: this.palettes, error: 'palettes ' + this.palettes + ': ' + (error instanceof Error ? error.message : String(error)) };
      }
    }
    this.send(chr, palettes);
  }

  dispose(): void {
    if (this.timer) {
      clearTimeout(this.timer);
    }
    this.subscription.dispose();
    this.watched.forEach(w => w.dispose());
  }
}
