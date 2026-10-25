import { PaletteAsset } from './assets.js';
import { GraphicsSystem } from './system.js';

/**
 * The messages between the extension host and an editor's webview.
 *
 * The host owns the file: a JSON asset is the text of its TextDocument, a tile
 * set the bytes of its CustomDocument. A webview shows what the host sends and
 * answers an edit with the whole new asset (or the whole new tile data): the
 * host turns it into one undoable change. Strokes are drawn locally while the
 * mouse is down and sent once, when it is released, so one stroke is one undo
 * step. The host does not echo an edit back to the webview it came from.
 */

export type EditorKind = 'palette' | 'tiles' | 'screen' | 'map' | 'metasprite';

/** A tile set a JSON asset names, resolved and read by the host. */
export interface ChrResource {
  /** As the asset names it. */
  path: string;
  /** The tile data, or undefined when it cannot be read. */
  data?: number[];
  error?: string;
}

/** A palette file an asset names (or the tile editor shows its tiles in). */
export interface PaletteResource {
  path: string;
  asset?: PaletteAsset;
  error?: string;
}

/** Host → webview. */
export type HostMessage =
  /** First message, answering `ready`: the machine and the file. */
  | { type: 'init'; kind: EditorKind; system: GraphicsSystem; fileName: string; systemNote?: string }
  /** The JSON asset as the document now holds it; `error` when the text is not a usable asset. */
  | { type: 'asset'; asset?: unknown; error?: string }
  /** The tile set's bytes (tile editor). */
  | { type: 'chr'; data: number[] }
  /** The files the asset refers to, read again whenever they change on disk or in an editor. */
  | { type: 'resources'; chr?: ChrResource; palettes?: PaletteResource }
  /** The palette files of the workspace (tile editor's display palette). */
  | { type: 'paletteFiles'; files: Array<{ path: string; label: string }> }
  /** The palette to show tiles in (tile editor), undefined for greys. */
  | { type: 'displayPalette'; palettes?: PaletteResource }
  /** A one-line message for the status area. */
  | { type: 'notice'; text: string; level: 'info' | 'error' };

/** Webview → host. */
export type WebviewMessage =
  | { type: 'ready' }
  /** A JSON asset edited: the whole new asset. */
  | { type: 'edit'; label: string; asset: unknown }
  /** A tile set edited: the whole new data (it may have grown). */
  | { type: 'chrEdit'; label: string; data: number[] }
  /** Tile editor: show the tiles in this palette file (workspace-relative path), or greys. */
  | { type: 'choosePalette'; path?: string }
  /** Ask the host to let the user choose a file for a field of the asset (`chr`, `palettes`). */
  | { type: 'chooseFile'; field: 'chr' | 'palettes' }
  /** Open a file the asset names, in its own editor. */
  | { type: 'openFile'; path: string }
  /** Open the asset as text. */
  | { type: 'openAsText' };
