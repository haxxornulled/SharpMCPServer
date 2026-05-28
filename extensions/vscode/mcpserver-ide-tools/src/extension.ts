import * as vscode from "vscode";

const CHAT_CONFIGURATION_NAME = "MCPServer: Chat Console";
const HOST_CONFIGURATION_NAME = "MCPServer: Host";
const MISSING_WORKSPACE_MESSAGE = "Open the MCPServer repository folder first.";
const UNKNOWN_DEBUG_LAUNCH_FAILURE = "Unknown VS Code debug launch failure.";

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand("mcpserver.openChatConsole", async () => {
      await openDebugConfiguration(CHAT_CONFIGURATION_NAME);
    }),
    vscode.commands.registerCommand("mcpserver.openHost", async () => {
      await openDebugConfiguration(HOST_CONFIGURATION_NAME);
    }),
    vscode.commands.registerCommand("mcpserver.showWorkspaceRoot", async () => {
      const root = getWorkspaceRoot();
      if (root === undefined) {
        await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
        return;
      }

      await vscode.window.showInformationMessage(`MCPServer workspace root: ${root}`);
    })
  );
}

export function deactivate(): void {
  // No cleanup needed yet.
}

async function openDebugConfiguration(configurationName: string): Promise<void> {
  try {
    const workspaceFolder = getPrimaryWorkspaceFolder();
    if (workspaceFolder === undefined) {
      await vscode.window.showWarningMessage(MISSING_WORKSPACE_MESSAGE);
      return;
    }

    const launched = await vscode.debug.startDebugging(workspaceFolder, configurationName);
    if (!launched) {
      await vscode.window.showErrorMessage(`Could not start the "${configurationName}" debug configuration.`);
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : UNKNOWN_DEBUG_LAUNCH_FAILURE;
    await vscode.window.showErrorMessage(message);
  }
}

function getPrimaryWorkspaceFolder(): vscode.WorkspaceFolder | undefined {
  return vscode.workspace.workspaceFolders?.[0];
}

function getWorkspaceRoot(): string | undefined {
  return getPrimaryWorkspaceFolder()?.uri.fsPath;
}
