# IDE Extensions

This repository keeps the IDE-facing extension scaffolds close to the code they help with.

## Agent Bubble

Both IDE extensions are just control surfaces for the same agent bubble described in the root README.

- They should stay thin.
- They should launch or surface host-owned workflows, not reimplement them.
- They should never bypass workspace-root scoping, sandbox policy, or approval gates.
- If an IDE feature needs real behavior, move that behavior into shared application or infrastructure code first.

## Visual Studio

- `extensions/visualstudio/MCPServer.VisualStudio.Vsix` is the shipping Visual Studio 2026-compatible VSSDK VSIX. It launches the MCP chat console, host, and workspace dashboard from a classic shell package.
- In Visual Studio, the extension menu appears under `Extensions > MCPServer`, with commands for the chat console, host, and workspace dashboard.
- The Visual Studio extension keeps the shell layer thin. Commands, tool windows, and workspace adapters resolve shared services through explicit constructor injection instead of manual composition inside the package class.
- The repo root includes [`.vsconfig`](../.vsconfig) so Visual Studio can prompt for the current extension-development workload.

### Visual Studio VSIX build and install

Build the installable VSIX with the VSSDK packaging targets enabled:

```powershell
dotnet msbuild .\extensions\visualstudio\MCPServer.VisualStudio.Vsix\MCPServer.VisualStudio.Vsix.csproj /t:Build /p:Configuration=Release /p:VSSDKBuildToolsAutoSetup=true
```

The expected release package is:

```text
extensions\visualstudio\MCPServer.VisualStudio.Vsix\bin\Release\net472\MCPServer.VisualStudio.Vsix.vsix
```

For a clean VS 2026 Enterprise install, target the installed product explicitly. `VSIXInstaller.exe` is picky about command-line parsing: quote the value after each colon, and prefer a no-space temporary VSIX path such as `C:\VSIXTemp\MCPServer.VisualStudio.Vsix.vsix`.

```powershell
$installer = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe'
$vsix = 'C:\VSIXTemp\MCPServer.VisualStudio.Vsix.vsix'
$appIdInstallPath = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\devenv.exe'
$appIdName = 'Visual Studio Enterprise 2026'
$skuName = 'Enterprise'
$skuVersion = '18.6.11819.183'

& $installer /quiet /appIdInstallPath:"$appIdInstallPath" /appIdName:"$appIdName" /skuName:$skuName /skuVersion:$skuVersion /uninstall:MCPServer.VisualStudio.Vsix
& $installer /quiet /appIdInstallPath:"$appIdInstallPath" /appIdName:"$appIdName" /skuName:$skuName /skuVersion:$skuVersion "$vsix"
```

Installer notes:

- Exit code `0` means the install completed.
- Exit code `1002` on uninstall means the extension was not installed yet; that is acceptable before a clean install.
- Exit code `2001` means the installer parsed the command line as invalid and usually shows the usage dialog. Check quoting first, especially `/appIdInstallPath:"..."`, `/appIdName:"..."`, and paths containing spaces.
- The VSIX manifest must include `ProductArchitecture` for each installation target and a `Prerequisites` section. The current package targets VS `[17.0,)` for Community, Pro, and Enterprise on `amd64` and `arm64`.

## VS Code

- `extensions/vscode/mcpserver-ide-tools` is a lightweight VS Code extension scaffold that starts the same repo-owned debug profiles from the command palette.
- The root [`.vscode/launch.json`](../.vscode/launch.json) and [`.vscode/tasks.json`](../.vscode/tasks.json) files wire that extension into the workspace.

The intended workflow is still project-driven:

- use the repo launch profiles for the console and host,
- use the Visual Studio SDK extension for Visual Studio integration,
- use the VS Code extension for VS Code integration.
