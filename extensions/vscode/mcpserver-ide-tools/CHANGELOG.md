# Changelog

## 0.1.0

- Initial VS Code extension packaging for MCPServer IDE launch commands.
- Add commands to open the chat console, open the host, and display the active workspace root.
- Keep the extension focused on repository-owned debug profiles rather than custom runtime logic.

## 0.1.1

- Add a workspace status bar item that tracks the active workspace folder.
- Add workspace action commands for copy and explorer launch.
- Add a quick action menu so the extension behaves like a real control surface instead of a bare scaffold.
- Add an MCPServer sidebar view so the extension is visible and discoverable as soon as VS Code loads.
- Launch the chat console and host in a terminal instead of the debug/build pipeline to avoid file-lock churn.
- Remove the redundant visible host launcher so the extension focuses on chat and workspace control.
- Replace the extension-host restart command with a local MCPServer state refresh command.

## 0.1.2

- Launch the chat console through a repo-local launcher wrapper so the visible terminal command stays short and readable.
- Show a friendly build message when the console or host outputs are missing instead of dumping a long `dotnet exec` line into the terminal.
- Keep the refresh action local to MCPServer state instead of restarting the VS Code extension host.

## 0.1.4

- Replace the shell wrapper with a small C# `MCPServer.ChatLauncher` project so the terminal launcher stays in the repository’s main language.
- Keep the launcher banner readable for junior developers while still delegating the real chat console startup to the built console assembly.
- Keep the refresh action local to MCPServer state instead of restarting the VS Code extension host.

## 0.1.5

- Add an inference provider section to the sidebar so lmstudio and ollama models can be changed from a dropdown instead of hand-editing `appsettings.json`.
- Fetch live `/v1/models` lists from the configured provider base address and persist the chosen model back into the host config.
- Expose a command palette action for choosing an inference provider model directly.

## 0.1.6

- Add routing controls for tandem validation, second-opinion flow, and provider priority so developers can choose the pair and dealbreaker model from VS Code.
- Add provider performance tuning commands for max tokens, temperature, top-p, top-k, repeat penalty, seed, context length, and keep-alive.
- Switch Ollama to its native chat API so local tuning fields can actually influence the request body and performance testing gets the real wire format.
- Open the MCPServer sidebar on first activation and show a visible prompt so the new UI is obvious without hunting for it.
