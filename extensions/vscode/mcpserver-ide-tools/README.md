# MCPServer IDE Tools

MCPServer IDE Tools is a small VS Code extension for launching the repository-owned MCPServer workflows from the command palette, exposing the active workspace root in the status bar, surfacing a visible MCPServer sidebar, and letting you pick live inference models or API keys for local or cloud providers from the UI.

## Commands

- `MCPServer: Open Chat Console`
- `MCPServer: Configure Inference Provider Model`
- `MCPServer: Configure Inference Routing`
- `MCPServer: Configure Inference Provider Priority`
- `MCPServer: Configure Inference Provider Performance`
- `MCPServer: Configure Inference Provider API Key`
- `MCPServer: Show Workspace Root`
- `MCPServer: Copy Workspace Root`
- `MCPServer: Open Workspace Root in Explorer`
- `MCPServer: Refresh MCPServer State`
- `MCPServer: Workspace Actions`

## Behavior

- The extension expects the MCPServer repository folder to be open as the active VS Code workspace.
- It does not implement the chat console or the host itself.
- It simply opens the repo-owned workflows so the workspace rules stay owned by the repository.
- The chat launcher uses the repo-owned C# `MCPServer.ChatLauncher` project so the visible terminal command stays short and the missing-build case is readable instead of dumping a long `dotnet exec` line into the terminal.
- The status bar shows the active workspace folder and opens a quick action menu when clicked.
- The `MCPServer` activity bar view shows the workspace root and the same launch actions in a place that is hard to miss.
- The first time the extension activates in a workspace, it opens the `MCPServer` sidebar and shows a visible prompt so the UI is easy to find.
- The `Inference Providers` section in the sidebar shows the configured providers and opens a live model dropdown for each provider so developers can switch provider models without hand-editing JSON. It also supports cloud providers that need an API key or bearer token for live model discovery.
- The `Inference Routing` item in the sidebar and command palette lets developers set tandem validation, the validator model, and provider priority so the second-opinion and dealbreaker flows stay explicit.
- The provider performance command lets developers tune per-provider defaults like max tokens, temperature, top-p, top-k, repeat penalty, seed, context length, and keep-alive without editing JSON by hand.
- The refresh command only re-reads MCPServer state and redraws the sidebar. It does not restart the VS Code extension host.

## Packaging

This extension is intentionally thin. The published VSIX contains the compiled output and the metadata needed to install it, not the TypeScript source tree.
