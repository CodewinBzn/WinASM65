import { AssetKind } from './assets.js';
import { GraphicsSystem, attributeColumns } from './system.js';

/**
 * The layout WinASM65.Graphics gives asset files (AssetJson.Serialize): two
 * spaces, `\n`, a final newline, small flat objects on one line, and number
 * arrays laid out as the picture they describe (a screen's tiles one screen row
 * per line). The designer's `format` request is the reference; this copy is
 * what the extension falls back to when the designer cannot be reached, so a
 * file saved either way reads the same.
 */
const COMPACT_LIMIT = 100;
const DEFAULT_ROW = 16;

type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

/** Numbers per line of a long number array of an asset, by property name (0: no fixed row). */
export function rowLengths(kind: AssetKind, asset: unknown, system: GraphicsSystem | undefined): (property: string) => number {
  if (kind === 'screen') {
    const tiles = system ? system.screenWidthTiles : 32;
    const attributes = system ? attributeColumns(system) : 16;
    return property => (property === 'tiles' ? tiles : property === 'attributes' ? attributes : 0);
  }
  if (kind === 'map') {
    const width = (asset as { width?: number }).width ?? 0;
    return property => (property === 'cells' ? width : 0);
  }
  return () => 0;
}

/** The text of an asset file, as the designer writes it. */
export function formatAsset(kind: AssetKind, asset: unknown, system: GraphicsSystem | undefined): string {
  const node = withoutNulls(JSON.parse(JSON.stringify(asset)) as Json);
  return write(node, 0, undefined, rowLengths(kind, asset, system), true) + '\n';
}

function withoutNulls(node: Json): Json {
  if (Array.isArray(node)) {
    return node.map(withoutNulls);
  }
  if (node !== null && typeof node === 'object') {
    const result: { [key: string]: Json } = {};
    for (const [key, value] of Object.entries(node)) {
      if (value !== null) {
        result[key] = withoutNulls(value);
      }
    }
    return result;
  }
  return node;
}

function isObject(node: Json): node is { [key: string]: Json } {
  return node !== null && typeof node === 'object' && !Array.isArray(node);
}

function allPlain(array: Json[]): boolean {
  return array.every(item => item === null || typeof item !== 'object');
}

function isFlat(object: { [key: string]: Json }): boolean {
  return Object.values(object).every(value => !isObject(value) && !(Array.isArray(value) && !allPlain(value)));
}

function compact(node: Json): string {
  if (Array.isArray(node)) {
    return '[' + node.map(compact).join(', ') + ']';
  }
  if (isObject(node)) {
    return '{ ' + Object.entries(node).map(([key, value]) => JSON.stringify(key) + ': ' + compact(value)).join(', ') + ' }';
  }
  return JSON.stringify(node);
}

function write(node: Json, indent: number, property: string | undefined, rowLength: (property: string) => number, top: boolean): string {
  const pad = (n: number) => ' '.repeat(n);
  if (isObject(node)) {
    const entries = Object.entries(node);
    if (entries.length === 0) {
      return '{}';
    }
    if (!top && isFlat(node)) {
      const text = compact(node);
      if (text.length + indent <= COMPACT_LIMIT) {
        return text;
      }
    }
    let text = '{\n';
    entries.forEach(([key, value], i) => {
      text += pad(indent + 2) + JSON.stringify(key) + ': ' + write(value, indent + 2, key, rowLength, false);
      text += (i < entries.length - 1 ? ',' : '') + '\n';
    });
    return text + pad(indent) + '}';
  }
  if (Array.isArray(node)) {
    if (node.length === 0) {
      return '[]';
    }
    if (allPlain(node)) {
      const text = compact(node);
      let row = property === undefined ? 0 : rowLength(property);
      if (row <= 0 && text.length + indent + (property ?? '').length <= COMPACT_LIMIT) {
        return text;
      }
      if (row <= 0) {
        row = DEFAULT_ROW;
      }
      let out = '[\n';
      for (let start = 0; start < node.length; start += row) {
        const end = Math.min(node.length, start + row);
        out += pad(indent + 2);
        for (let k = start; k < end; k++) {
          out += JSON.stringify(node[k]);
          if (k < node.length - 1) {
            out += k < end - 1 ? ', ' : ',';
          }
        }
        out += '\n';
      }
      return out + pad(indent) + ']';
    }
    let out = '[\n';
    node.forEach((item, k) => {
      out += pad(indent + 2) + write(item, indent + 2, undefined, rowLength, false) + (k < node.length - 1 ? ',' : '') + '\n';
    });
    return out + pad(indent) + ']';
  }
  return JSON.stringify(node);
}

/**
 * Where the value a JSON path names (`$.palettes[2].colors[1]`, as the
 * designer's diagnostics give it) starts in a JSON text: the offset of the
 * value, or of the deepest part of the path found. Comments are skipped, as
 * the designer allows them.
 */
export function locateJsonPath(text: string, path: string): number {
  const target = parsePath(path);
  let pos = 0;
  let best = 0;
  let bestDepth = -1;

  const skip = () => {
    for (;;) {
      while (pos < text.length && /\s/.test(text[pos])) pos++;
      if (text.startsWith('//', pos)) {
        const end = text.indexOf('\n', pos);
        pos = end < 0 ? text.length : end + 1;
      } else if (text.startsWith('/*', pos)) {
        const end = text.indexOf('*/', pos + 2);
        pos = end < 0 ? text.length : end + 2;
      } else {
        return;
      }
    }
  };

  const readString = (): string => {
    // pos is on the opening quote.
    let out = '';
    pos++;
    while (pos < text.length && text[pos] !== '"') {
      if (text[pos] === '\\') {
        const next = text[pos + 1];
        if (next === 'u') {
          out += String.fromCharCode(parseInt(text.substr(pos + 2, 4), 16));
          pos += 6;
          continue;
        }
        out += next === 'n' ? '\n' : next === 't' ? '\t' : next === 'r' ? '\r' : next === 'b' ? '\b' : next === 'f' ? '\f' : next;
        pos += 2;
        continue;
      }
      out += text[pos++];
    }
    pos++;
    return out;
  };

  // Walks one value; `depth` segments of the target matched so far (-1: off the path).
  const value = (depth: number): boolean => {
    skip();
    if (depth >= 0 && depth > bestDepth) {
      bestDepth = depth;
      best = pos;
    }
    if (depth === target.length) {
      return true;
    }
    const c = text[pos];
    if (c === '{') {
      pos++;
      for (;;) {
        skip();
        if (pos >= text.length) return false;
        if (text[pos] === '}') { pos++; return false; }
        if (text[pos] === ',') { pos++; continue; }
        if (text[pos] !== '"') { pos++; continue; }
        const key = readString();
        skip();
        if (text[pos] === ':') pos++;
        const on = depth >= 0 && depth < target.length && target[depth] === key;
        if (value(on ? depth + 1 : -1)) return true;
      }
    }
    if (c === '[') {
      pos++;
      let index = 0;
      for (;;) {
        skip();
        if (pos >= text.length) return false;
        if (text[pos] === ']') { pos++; return false; }
        if (text[pos] === ',') { pos++; index++; continue; }
        const on = depth >= 0 && depth < target.length && target[depth] === index;
        if (value(on ? depth + 1 : -1)) return true;
      }
    }
    if (c === '"') {
      readString();
      return false;
    }
    while (pos < text.length && !/[\s,\]}]/.test(text[pos])) pos++;
    return false;
  };

  value(0);
  return best;
}

/** `$.a[2].b` or `$['a'][2]` as segments: ['a', 2, 'b']. */
export function parsePath(path: string): Array<string | number> {
  const segments: Array<string | number> = [];
  const pattern = /\.([A-Za-z_$][\w$]*)|\[(\d+)\]|\['((?:[^'\\]|\\.)*)'\]/g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(path)) !== null) {
    if (match[1] !== undefined) segments.push(match[1]);
    else if (match[2] !== undefined) segments.push(Number(match[2]));
    else segments.push(match[3].replace(/\\(.)/g, '$1'));
  }
  return segments;
}

/** The 0-based line of an offset in a text. */
export function lineOf(text: string, offset: number): number {
  let line = 0;
  for (let i = 0; i < offset && i < text.length; i++) {
    if (text.charCodeAt(i) === 10) line++;
  }
  return line;
}

/**
 * Parses JSON as the designer reads it: comments and trailing commas allowed.
 * Throws a SyntaxError when the text is not JSON.
 */
export function parseJson(text: string): unknown {
  let out = '';
  let i = 0;
  while (i < text.length) {
    const c = text[i];
    if (c === '"') {
      let j = i + 1;
      while (j < text.length && text[j] !== '"') {
        j += text[j] === '\\' ? 2 : 1;
      }
      out += text.slice(i, j + 1);
      i = j + 1;
    } else if (text.startsWith('//', i)) {
      const end = text.indexOf('\n', i);
      i = end < 0 ? text.length : end;
    } else if (text.startsWith('/*', i)) {
      const end = text.indexOf('*/', i + 2);
      i = end < 0 ? text.length : end + 2;
    } else if (c === ',') {
      // A comma followed only by blanks and comments up to a closing bracket is dropped.
      let j = i + 1;
      for (;;) {
        while (j < text.length && /\s/.test(text[j])) j++;
        if (text.startsWith('//', j)) {
          const end = text.indexOf('\n', j);
          j = end < 0 ? text.length : end;
        } else if (text.startsWith('/*', j)) {
          const end = text.indexOf('*/', j + 2);
          j = end < 0 ? text.length : end + 2;
        } else {
          break;
        }
      }
      if (text[j] !== '}' && text[j] !== ']') {
        out += c;
      }
      i++;
    } else {
      out += c;
      i++;
    }
  }
  return JSON.parse(out);
}
