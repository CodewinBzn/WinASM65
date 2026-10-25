import * as vscode from 'vscode';

/** The folder webview scripts load from, and the only one a webview may read. */
export function webviewRoot(extensionUri: vscode.Uri): vscode.Uri {
  return vscode.Uri.joinPath(extensionUri, 'out', 'webview');
}

export function webviewOptions(extensionUri: vscode.Uri): vscode.WebviewOptions {
  return { enableScripts: true, localResourceRoots: [webviewRoot(extensionUri)] };
}

/**
 * The page of an editor: the shared styles and one ES module from
 * out/webview/webview/<script>.js, which builds the editor in #app. Modules it
 * imports come from the same folder, so the script source allows the webview's
 * own origin besides the nonce.
 */
export function editorHtml(webview: vscode.Webview, extensionUri: vscode.Uri, script: string, title: string): string {
  const nonce = Array.from({ length: 32 }, () => Math.floor(Math.random() * 36).toString(36)).join('');
  const src = webview.asWebviewUri(vscode.Uri.joinPath(webviewRoot(extensionUri), 'webview', script + '.js'));
  const csp = [
    "default-src 'none'",
    `img-src ${webview.cspSource} data: blob:`,
    `style-src 'nonce-${nonce}'`,
    `script-src 'nonce-${nonce}' ${webview.cspSource}`
  ].join('; ');
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(title)}</title>
<style nonce="${nonce}">${STYLES}</style>
</head>
<body>
<div id="app" class="app"><div class="loading">Loading…</div></div>
<script type="module" nonce="${nonce}" src="${src}"></script>
</body>
</html>`;
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
}

/** One stylesheet for every editor, in the theme's colours. */
const STYLES = `
* { box-sizing: border-box; }
html, body { height: 100%; margin: 0; padding: 0; overflow: hidden; }
body {
  font-family: var(--vscode-font-family); font-size: var(--vscode-font-size, 13px);
  color: var(--vscode-foreground); background: var(--vscode-editor-background);
}
.app { display: flex; flex-direction: column; height: 100vh; }
.loading { padding: 16px; color: var(--vscode-descriptionForeground); }

.toolbar {
  display: flex; flex-wrap: wrap; align-items: center; gap: 4px 6px; padding: 6px 10px;
  border-bottom: 1px solid var(--vscode-panel-border, var(--vscode-widget-border, transparent));
  background: var(--vscode-sideBar-background, var(--vscode-editor-background));
}
.toolbar .sep { width: 1px; align-self: stretch; margin: 2px 4px; background: var(--vscode-panel-border, var(--vscode-widget-border, #8884)); }
.toolbar label { display: inline-flex; align-items: center; gap: 4px; color: var(--vscode-descriptionForeground); white-space: nowrap; }
.group { display: inline-flex; gap: 2px; align-items: center; }

button {
  font: inherit; color: var(--vscode-button-secondaryForeground, var(--vscode-foreground));
  background: var(--vscode-button-secondaryBackground, transparent);
  border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px;
  padding: 2px 8px; min-height: 24px; cursor: pointer; white-space: nowrap;
}
button:hover { background: var(--vscode-button-secondaryHoverBackground, var(--vscode-toolbar-hoverBackground)); }
button.primary { color: var(--vscode-button-foreground); background: var(--vscode-button-background); }
button.primary:hover { background: var(--vscode-button-hoverBackground); }
button.active, button[aria-pressed="true"] {
  color: var(--vscode-button-foreground); background: var(--vscode-button-background);
  outline: 1px solid var(--vscode-focusBorder); outline-offset: -1px;
}
button:disabled { opacity: .5; cursor: default; }
button:focus-visible, select:focus-visible, input:focus-visible { outline: 1px solid var(--vscode-focusBorder); outline-offset: -1px; }

select, input[type="text"], input[type="number"] {
  font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border, var(--vscode-dropdown-border, transparent)); border-radius: 2px;
  padding: 2px 4px; min-height: 24px;
}
select { color: var(--vscode-dropdown-foreground); background: var(--vscode-dropdown-background); border-color: var(--vscode-dropdown-border, transparent); }
input[type="number"] { width: 5em; }
input[type="checkbox"] { accent-color: var(--vscode-checkbox-background, var(--vscode-button-background)); margin: 0; }

.body { flex: 1; display: flex; min-height: 0; }
.side {
  flex: none; display: flex; flex-direction: column; gap: 10px; padding: 10px; overflow: auto;
  border-right: 1px solid var(--vscode-panel-border, var(--vscode-widget-border, transparent));
}
.main { flex: 1; overflow: auto; padding: 12px; min-width: 0; }
.scroll { overflow: auto; }
h3 {
  margin: 0 0 6px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em;
  color: var(--vscode-descriptionForeground);
}
.hint { color: var(--vscode-descriptionForeground); font-size: 12px; }
.mono { font-family: var(--vscode-editor-font-family); font-size: var(--vscode-editor-font-size, 13px); }

canvas { image-rendering: pixelated; image-rendering: crisp-edges; display: block; }
.canvas-frame { display: inline-block; position: relative; line-height: 0;
  border: 1px solid var(--vscode-panel-border, var(--vscode-widget-border, #8886)); }
.checker { background-color: #808080;
  background-image: linear-gradient(45deg, #999 25%, transparent 25%), linear-gradient(-45deg, #999 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, #999 75%), linear-gradient(-45deg, transparent 75%, #999 75%);
  background-size: 16px 16px; background-position: 0 0, 0 8px, 8px -8px, -8px 0; }

.status {
  display: flex; gap: 16px; padding: 3px 10px; min-height: 22px; align-items: center;
  border-top: 1px solid var(--vscode-panel-border, var(--vscode-widget-border, transparent));
  color: var(--vscode-descriptionForeground); font-size: 12px; white-space: nowrap; overflow: hidden;
}
.status .grow { flex: 1; }
.status .error, .error { color: var(--vscode-errorForeground); }
.banner {
  padding: 6px 10px; border-bottom: 1px solid var(--vscode-inputValidation-warningBorder, #cc0);
  background: var(--vscode-inputValidation-warningBackground, transparent); color: var(--vscode-foreground);
}
.banner.error { border-color: var(--vscode-inputValidation-errorBorder, #c00); background: var(--vscode-inputValidation-errorBackground, transparent); }
.banner button { margin-left: 8px; }
.hidden { display: none !important; }

.swatch {
  width: 28px; height: 28px; border-radius: 2px; cursor: pointer; position: relative; padding: 0; min-height: 0;
  border: 1px solid var(--vscode-widget-border, #8886);
}
.swatch.selected { outline: 2px solid var(--vscode-focusBorder); outline-offset: 1px; z-index: 1; }
.swatch .tag {
  position: absolute; left: 1px; bottom: 0; font-size: 9px; line-height: 11px; padding: 0 1px;
  font-family: var(--vscode-editor-font-family); background: #0009; color: #fff; border-radius: 1px; pointer-events: none;
}
.field { display: flex; flex-direction: column; gap: 3px; }
.field > span { color: var(--vscode-descriptionForeground); font-size: 12px; }
.row { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
.props { display: grid; grid-template-columns: auto 1fr auto; gap: 4px 6px; align-items: center; }
.props > span { color: var(--vscode-descriptionForeground); font-size: 12px; }
.props input[type="text"] { width: 100%; min-width: 0; }
.row.spread { justify-content: space-between; margin-bottom: 6px; }
.row.spread h3 { margin: 0; }
.row.top { align-items: flex-start; flex-wrap: nowrap; }
.grow { flex: 1; min-width: 0; }
section { min-width: 0; }

/* A palette as a button: its number and its colours. */
.palette-button { display: inline-flex; align-items: center; gap: 5px; padding: 2px 5px; }
.palette-button .strip { display: inline-flex; border: 1px solid var(--vscode-widget-border, #8886); }
.palette-button .strip span { width: 9px; height: 14px; }
.colours { gap: 4px; }
.colours .swatch { width: 24px; height: 24px; }

/* Palette editor */
.palette-layout { display: flex; flex-wrap: wrap; gap: 28px; align-items: flex-start; }
.palettes { display: flex; flex-direction: column; gap: 4px; }
.group-title { margin: 8px 0 2px; color: var(--vscode-descriptionForeground); font-size: 12px; }
.group-title:first-child { margin-top: 0; }
.palette-row { display: flex; align-items: center; gap: 8px; padding: 3px 6px; border-radius: 3px; }
.palette-row.current { background: var(--vscode-list-inactiveSelectionBackground, #8882); }
.palette-index { width: 1.2em; text-align: right; color: var(--vscode-descriptionForeground); }
.palette-name { width: 10em; }
.swatches { display: flex; gap: 6px; }
.swatch.big { width: 44px; height: 32px; }
.swatch.big .tag { font-size: 10px; line-height: 12px; }
.swatch.shared::after { content: ''; position: absolute; inset: 3px; border: 1px dashed currentColor; opacity: .5; border-radius: 1px; pointer-events: none; }
.swatch .mark {
  position: absolute; right: -5px; top: -7px; font-size: 11px; line-height: 14px; width: 14px; height: 14px; border-radius: 7px;
  background: var(--vscode-editorWarning-foreground, #cca700); color: #000; text-align: center; font-weight: bold;
}
.master { display: grid; gap: 2px; margin-bottom: 8px; }
.master-cell {
  width: 34px; height: 30px; padding: 0; min-height: 0; border: 1px solid transparent; border-radius: 2px;
  font-family: var(--vscode-editor-font-family); font-size: 10px; display: flex; align-items: flex-end; justify-content: flex-start;
}
.master-cell span { padding: 0 2px; opacity: .85; }
.master-cell:hover { outline: 1px solid var(--vscode-focusBorder); outline-offset: 0; }
.master-cell.selected { outline: 2px solid var(--vscode-focusBorder); outline-offset: 1px; z-index: 1; }
.master-cell.avoided { background-image: linear-gradient(135deg, transparent 46%, #f448 46%, #f448 54%, transparent 54%) !important; }
.palette-body .main { padding: 14px 16px; }
.palette-body p.hint { max-width: 560px; }

/* Tile, screen and map editors */
.sheet-side { width: auto; max-width: 50%; }
.editor-host { display: flex; align-items: center; justify-content: center; overflow: hidden; }
.screen-side { width: 300px; }
.map-side { width: 300px; }
.metatile-editor .field { gap: 4px; }
.metatile-editor .group { flex-wrap: wrap; gap: 4px; }
.side .canvas-frame { max-width: 100%; }
`;
