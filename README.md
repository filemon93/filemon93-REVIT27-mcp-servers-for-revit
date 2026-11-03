# Revit MCP 27

Connect local MCP agents to Autodesk Revit 2027 on Windows x64. This fork includes model inspection/editing tools and associative plan dimensioning.

**[Download the ready-to-install package](https://github.com/filemon93/filemon93-REVIT27-mcp-servers-for-revit/releases/latest)** · **[Instrukcja instalacji i podłączenia agentów po polsku](docs/START-PL.md)** · [Dimension tools](docs/DIMENSIONING.md) · [Validation](docs/VALIDATION.md)

## Quick start for employees

1. Download `RevitMCP27-1.1.0-Windows-x64.zip` from Releases and extract it locally. Choose the named asset, not GitHub's automatic Source code archive.
2. Close Revit 2027 and run `Install-2027.bat`. No administrator, Node installation, npm, SDK or GitHub account is required. The package includes its own Node 22 runtime and production dependencies.
3. Merge the generated `%LOCALAPPDATA%\RevitMCP27\agent-config.json` into your agent's MCP configuration, or use `agent-config.toml` for Codex. The generated files contain the actual installation paths. Restart the MCP connection after configuration changes.
4. Start Revit, open a model, then choose **Add-Ins → Revit MCP Switch**. Close the confirmation dialog.
5. Ask the agent to call `get_current_view_info` and `get_dimension_types`.

The agent launches the MCP server as a STDIO process. That server connects to the Revit add-in at `127.0.0.1:8080`. Both must run on the same Windows computer. Only one Revit bridge can use this port. The bridge listens on loopback only.

**Do not use `npx mcp-server-for-revit` for this fork:** that npm package belongs to upstream and does not distribute these changes. This fork's release workflow publishes GitHub ZIP assets, not upstream npm packages.

## Plan dimensioning

| Tool | Purpose |
|---|---|
| `get_dimension_types` | List linear dimension styles in the active model |
| `create_dimensions` | Create dimensions from explicit stable references or selected element references; respects linePoint and reports per-item results |
| `dimension_walls_by_guides` | Create wall dimension chains along straight visible guide lines/reference planes, with finish/core selection and a minimum segment threshold |

Guide chains support straight vertical basic walls in the active document. Core mode uses validated internal Revit references and remains experimental. Linked, curved, curtain, stacked and slanted walls and automatic door/window jamb dimensions are outside this release's guide-chain scope. Repeating a request creates another series. Successful items remain when another item fails; always inspect the detailed results.

Point-only dimension requests no longer guess nearby elements. Supply references or element IDs. See [parameters and examples](docs/DIMENSIONING.md).

## Other tools

The fork retains inspection (`get_current_view_info`, `get_current_view_elements`, `get_selected_elements`, `get_element_info`, `get_groups`, family types, statistics and material quantities), creation (walls/line-based elements, point/surface elements, rooms, levels, grids and framing systems), editing (parameters, type changes, geometry joins/cuts, colors and element operations), tags, room export, local data storage and C# execution tools. The server advertises the complete tool list to the MCP client.

Model-changing tools operate on the open Revit document. They do not save or synchronize the project automatically. Follow company policies for model access and the agent provider's data handling.

## Development

Requires .NET 10 SDK and Node.js 22 for the supported R27 package:

```powershell
dotnet build plugin/RevitMCPPlugin.csproj -c "Release R27"
dotnet build commandset/RevitMCPCommandSet.csproj -c "Release R27"
cd server
npm ci
npm run build
npm test
```

The TypeScript server lives in `server`, the Revit bridge in `plugin`, and the Revit command implementations in `commandset`. New commands use the existing ExternalEvent delegate executor so Revit API calls run on its UI thread. Tools register automatically from `server/src/tools`; Revit command definitions are in `command.json`.

Older target configurations remain in the project for upstream compatibility; this release distributes and validates Revit 2027 only. Integration tests in `tests/commandset` retain their upstream configuration. The new live dimension fixture is documented in [VALIDATION.md](docs/VALIDATION.md).

## Packaging and releases

```powershell
./scripts/Build-Package.ps1 -Version 1.1.0
```

This builds both C# projects and the server, runs STDIO tests and the production dependency audit, stages production dependencies with a matching Node runtime, excludes Autodesk Revit API DLLs, and produces a ZIP plus SHA256 under `artifacts`. Existing ZIP files are not overwritten. The installer verifies package hashes before installation, backs up previous user manifests, preserves unrelated add-in entries and refuses installation while Revit 2027 is open.

The GitHub Actions Release workflow runs on `v*` tags and publishes the same package plus Polish setup instructions. It does not publish to npm. Merge the tested changes before tagging. `scripts/release.ps1` requires a clean main checkout and matching package version; it never discards changes.

## Credits and license

Fork of [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit), based on the original revit-mcp, revit-mcp-plugin and revit-mcp-commandset projects. Guide dimensioning adapts the repository owner's JEMS Auto Wymiary engine. [MIT](LICENSE).
