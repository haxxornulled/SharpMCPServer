# Known Drift

This file exists to be honest about what is not fully clean yet.

## Current drift

- `MCPServer.Host.Sidecar` still wraps provider storage/vault types directly instead of depending only on higher-level provider services.
- `MCPServer.Host` still has direct composition knowledge of both SSH provider and SSH MCP adapter packages.
- Some sidecar implementation names still reflect earlier file-based behavior even though the provider is now SQLite-centered.

## Deliberate cleanup choices already made

- historical design notes that no longer described the repo were removed instead of rewritten into fiction;
- empty `DependencyInjection` extension shells were removed because they suggested supported composition APIs that did nothing;
- package versions were centralized to reduce scattered dependency definitions.
