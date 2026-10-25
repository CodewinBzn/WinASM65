import * as vscode from 'vscode';

/** The parts of VS Code's MCP API this extension uses (VS Code 1.101 and later). */
interface McpApi {
  registerMcpServerDefinitionProvider(id: string, provider: {
    provideMcpServerDefinitions(): unknown[];
  }): vscode.Disposable;
}

type McpStdioServerDefinitionConstructor = new (
  label: string, command: string, args: string[], env?: Record<string, string>, version?: string) => unknown;

/**
 * Offers WinASM65's MCP server (WinASM65.Tooling --mcp) to the agents inside VS
 * Code, so they can build, debug and edit graphics in the open workspace with the
 * same tools an external assistant gets (docs/site/ai.html).
 *
 * The API exists from VS Code 1.101 on; on an older VS Code nothing is registered
 * and the rest of the extension is unaffected.
 */
export function registerMcpServer(context: vscode.ExtensionContext,
  tooling: () => { command: string; args: string[] } | undefined, log: vscode.LogOutputChannel): void {
  const api = (vscode as unknown as { lm?: Partial<McpApi> }).lm;
  const definition = (vscode as unknown as { McpStdioServerDefinition?: McpStdioServerDefinitionConstructor }).McpStdioServerDefinition;
  if (!api || typeof api.registerMcpServerDefinitionProvider !== 'function' || !definition) {
    log.info('this VS Code has no MCP API: the WinASM65 MCP server is not offered to its agents');
    return;
  }
  context.subscriptions.push(api.registerMcpServerDefinitionProvider('winasm65.mcp', {
    provideMcpServerDefinitions: () => {
      const resolved = tooling();
      if (!resolved) {
        return [];
      }
      return [new definition('WinASM65', resolved.command, [...resolved.args, '--mcp'], {},
        String(context.extension.packageJSON.version))];
    }
  }));
  log.info('WinASM65 MCP server offered to VS Code agents');
}
