# Client Interaction Runtime

This repo now includes runtime plumbing for server-initiated client feature requests over stdio:

- `sampling/createMessage`
- `elicitation/create` (form and URL mode)
- `notifications/tasks/status`

Current implementation notes:

- The server can initiate client feature requests through `IMcpClientFeatureInvoker`.
- The stdio transport implements this through `StdioMcpClientFeatureTransport`.
- The server exposes testable tool surfaces:
  - `client.sample`
  - `client.elicit.form`
  - `client.elicit.url`
- Server-owned task status changes are emitted through `IMcpTaskStatusNotifier`.

Remaining parity work after this batch:

- End-to-end client-side handling for server-initiated requests in `MCPServer.Client`
- `notifications/tasks/status` emission for client-owned async task augmentation
- Streamable HTTP + authorization / OIDC coverage
