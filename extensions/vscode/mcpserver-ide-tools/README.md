# MCPServer IDE Tools

MCPServer IDE Tools is a small VS Code extension for launching the repository-owned MCPServer workflows from the command palette.

## Commands

- `MCPServer: Open Chat Console`
- `MCPServer: Open Host`
- `MCPServer: Show Workspace Root`

## Behavior

- The extension expects the MCPServer repository folder to be open as the active VS Code workspace.
- It does not implement the chat console or the host itself.
- It simply opens the repo's debug profiles so the workspace rules stay owned by the repository.

## Packaging

This extension is intentionally thin. The published VSIX contains the compiled output and the metadata needed to install it, not the TypeScript source tree.
