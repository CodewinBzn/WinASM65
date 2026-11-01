import * as assert from 'assert';
import * as path from 'path';
import * as vscode from 'vscode';
import type { ListingColumn } from '../listing';

async function waitFor<T>(what: string, probe: () => T | undefined, timeoutMs = 30000): Promise<T> {
  const end = Date.now() + timeoutMs;
  while (Date.now() < end) {
    const value = probe();
    if (value !== undefined) {
      return value;
    }
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error('timed out waiting for ' + what);
}

/** Runs inside VS Code: the extension as a user meets it, on example_nes_game. */
export async function run(): Promise<void> {
  const folder = vscode.workspace.workspaceFolders?.[0];
  assert.ok(folder, 'the example is open');
  const source = vscode.Uri.file(path.join(folder.uri.fsPath, 'engine', 'background.asm'));

  const document = await vscode.workspace.openTextDocument(source);
  await vscode.window.showTextDocument(document);
  assert.strictEqual(document.languageId, 'winasm65', 'background.asm is WinASM65 source');

  const extension = vscode.extensions.getExtension<{ listing: ListingColumn }>('codewinbzn.winasm65');
  assert.ok(extension, 'the extension is loaded');
  const api = await extension.activate();

  const commands = await vscode.commands.getCommands(true);
  for (const id of ['winasm65.showListing', 'winasm65.hideListing', 'winasm65.showRam']) {
    assert.ok(commands.includes(id), id + ' is registered');
  }

  await vscode.commands.executeCommand('winasm65.showListing');
  const rows = await waitFor('the listing column', () => api.listing.drawn.get(document.uri.toString()));
  assert.ok(rows > 40, 'background.asm has its listing: ' + rows + ' rows');

  await vscode.commands.executeCommand('winasm65.hideListing');
  console.log('PASS listing: ' + rows + ' rows drawn for background.asm');
}
