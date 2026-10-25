import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';
import { writeSamples } from './samples';

/**
 * Starts VS Code with this extension and the WinASM65 extension it depends on
 * (both under development) on a temporary folder of sample assets, and runs
 * src/test/suite.ts inside it. VSCODE_EXECUTABLE points at an installed VS Code
 * to avoid a download; otherwise the current stable one is fetched.
 */
async function main(): Promise<void> {
  const extensionDevelopmentPath = path.resolve(__dirname, '..', '..');
  const repo = path.resolve(extensionDevelopmentPath, '..');
  const extensionTestsPath = path.resolve(__dirname, 'suite');
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-graphics-test-'));
  writeSamples(workspace, repo);
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-graphics-vscode-'));

  // Run from a VS Code terminal, these would make the test instance a child of that VS Code.
  const executable = process.env.VSCODE_EXECUTABLE;
  for (const key of Object.keys(process.env)) {
    if (key.startsWith('VSCODE_') || key.startsWith('ELECTRON_')) {
      delete process.env[key];
    }
  }

  await runTests({
    vscodeExecutablePath: executable || undefined,
    extensionDevelopmentPath: [extensionDevelopmentPath, path.join(repo, 'vscode-extension')],
    extensionTestsPath,
    launchArgs: [workspace, '--disable-extensions', '--user-data-dir', userData, '--skip-welcome', '--skip-release-notes', '--disable-workspace-trust']
  });
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
