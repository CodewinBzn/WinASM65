import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { runTests } from '@vscode/test-electron';

/**
 * Starts VS Code with the extension under development on the NES example and
 * runs src/test/suite.ts inside it. VSCODE_EXECUTABLE points at an installed
 * VS Code to avoid a download; otherwise the current stable one is fetched.
 */
async function main(): Promise<void> {
  const extensionDevelopmentPath = path.resolve(__dirname, '..', '..');
  const extensionTestsPath = path.resolve(__dirname, 'suite');
  const workspace = path.resolve(extensionDevelopmentPath, '..', 'example_nes_game');
  const userData = fs.mkdtempSync(path.join(os.tmpdir(), 'winasm65-vscode-'));

  await runTests({
    vscodeExecutablePath: process.env.VSCODE_EXECUTABLE || undefined,
    extensionDevelopmentPath,
    extensionTestsPath,
    launchArgs: [workspace, '--disable-extensions', '--user-data-dir', userData, '--skip-welcome', '--skip-release-notes']
  });
}

main().catch(error => {
  console.error(error);
  process.exit(1);
});
