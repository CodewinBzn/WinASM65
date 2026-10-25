import { PaletteAsset } from '../shared/assets.js';
import { ChrResource, PaletteResource } from '../shared/protocol.js';
import { GraphicsSystem } from '../shared/system.js';
import { Banner, button, el, post, setInputValue, textInput } from './common.js';
import { TileCache } from './render.js';

/**
 * What an editor that draws with tiles shares (the screen and map editors, and
 * the metasprite editor of part 2): the tile set and palette file the asset
 * names, as the host reads them, and the panel of the asset's own fields.
 */
export class TileSources {
  cache: TileCache | undefined;
  palettes: PaletteAsset | undefined;
  chrPath: string | undefined;
  palettesPath: string | undefined;
  private chrError: string | undefined;
  private paletteError: string | undefined;

  constructor(private readonly system: GraphicsSystem, private readonly banner: Banner) {}

  /** Takes a `resources` message. */
  update(chr: ChrResource | undefined, palettes: PaletteResource | undefined): void {
    this.chrPath = chr?.path;
    this.chrError = chr?.error;
    this.cache = chr?.data ? new TileCache(this.system, Uint8Array.from(chr.data)) : undefined;
    this.palettesPath = palettes?.path;
    this.paletteError = palettes?.error;
    this.palettes = palettes?.asset;
    this.showProblems();
  }

  /** Says what is missing, across the top of the editor. */
  showProblems(chrNamed = true): void {
    const problems: string[] = [];
    if (!chrNamed && !this.chrPath) problems.push('No tile set: choose one in Tile set.');
    if (this.chrError) problems.push(this.chrError + '.');
    if (this.paletteError) problems.push(this.paletteError + ' (greys shown).');
    if (problems.length > 0) {
      this.banner.show(problems.join(' '));
    } else {
      this.banner.hide();
    }
  }
}

/** Name, prefix, tile set and palette file of an asset, each changed through `onChange`. */
export class AssetFields {
  readonly element = el('div', { className: 'props' });
  private readonly inputs = new Map<string, HTMLInputElement>();
  private readonly links = new Map<string, HTMLButtonElement>();

  constructor(fields: Array<'name' | 'prefix'>, onChange: (field: string, value: string) => void) {
    for (const field of fields) {
      const input = textInput('', field === 'prefix' ? 'none' : field, value => onChange(field, value));
      this.inputs.set(field, input);
      this.element.append(el('span', { text: field === 'name' ? 'Name' : 'Prefix' }), input, el('span'));
    }
    for (const field of ['chr', 'palettes'] as const) {
      const input = textInput('', field === 'chr' ? 'no tile set' : 'greys', value => onChange(field, value));
      input.title = field === 'chr' ? 'The tile set, relative to this file' : 'The palette file, relative to this file';
      this.inputs.set(field, input);
      const choose = button('…', 'Choose the ' + (field === 'chr' ? 'tile set' : 'palette file'), () => post({ type: 'chooseFile', field }));
      const open = button('Open', 'Open it in its editor', () => {
        const value = this.inputs.get(field)!.value;
        if (value) post({ type: 'openFile', path: value });
      });
      this.links.set(field, open);
      this.element.append(el('span', { text: field === 'chr' ? 'Tile set' : 'Palettes' }), input, el('span', { className: 'group' }, choose, open));
    }
  }

  set(values: Record<string, string | undefined>): void {
    for (const [field, input] of this.inputs) {
      setInputValue(input, values[field] ?? '');
    }
    for (const [field, open] of this.links) {
      open.disabled = !values[field];
    }
  }
}
