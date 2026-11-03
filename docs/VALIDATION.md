# Validation — 1.1.0

Validated on 2026-11-03.

- Release R27: plugin and command set compile against Revit 2027 / .NET 10.
- TypeScript build and Node test suite pass. A real MCP STDIO client initializes the server and verifies all three dimension tools are advertised; schema tests reject unsafe numeric IDs, infinite coordinates, empty guides and negative minimum lengths.
- npm audit reports zero vulnerabilities after updating dependencies to patched versions, including Zod 4.6.7. The obsolete rimraf build dependency was removed.
- Live Revit **2027.3**: 14 assertions pass against the actual production dimension-service source compiled into an isolated test assembly. The test creates a separate unsaved metric document, creates two compound walls and detail guides, and closes that document without saving. The active user's model is not used as a fixture.
- Live assertions: finish chain creation; four references/three segments; finish thickness; core chain creation; core thickness; reversed guides; direction-independent minimum filtering; per-guide partial failure; explicit wall dimension; linePoint offset; explicit partial results; point-only rejection; duplicate-reference rejection; greedy spacing behavior.

The live test entry point is `DimensionSmoke.Run(Autodesk.Revit.ApplicationServices.Application)` in `tests/dimensions`. Build with `dotnet build tests/dimensions/DimensionSmoke.csproj -c Release` and invoke from a Revit API context with no open transaction. The test assembly links the production sources to avoid replacing a loaded plugin. Do not invoke from a background thread. This is a reproducible integration fixture, not a standalone console test.

The tests do not certify every family, linked model, view-template configuration or Revit patch. Core references remain experimental even though the compound-wall fixture passes. This release does not claim support for other Revit years.

`Build-Package.ps1` builds and tests the STDIO server, installs production dependencies into a fresh staging directory, bundles matching Node 22, excludes Revit API DLLs and generates SHA256 checksums. `Install.ps1 -CheckOnly` verifies those payload hashes without changing the installation. Installation requires Revit 2027 to be closed; the installer never kills it or saves a model.
