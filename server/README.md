# Revit MCP 27 server

This server is distributed with the matching Revit 2027 add-in in [GitHub Releases](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/releases/latest).

Use the bundled Node executable and `build/index.js` as an MCP STDIO command. The installer generates absolute paths in `agent-config.json` and `agent-config.toml`. See [Polish setup instructions](../docs/START-PL.md).

Do not install this fork with the upstream `npx mcp-server-for-revit` command. That package is maintained separately.

Development: Node 22, `npm ci`, `npm run build`, `npm test`. Revit bridge: 127.0.0.1:8080.
