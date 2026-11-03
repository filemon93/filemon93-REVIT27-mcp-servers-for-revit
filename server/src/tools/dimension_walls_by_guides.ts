import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { sendRevitCommand } from "../utils/modelEditing.js";
import { guideDimensionSchema } from "../utils/dimensionSchemas.js";
export function registerDimensionWallsByGuidesTool(server: McpServer) {
  server.tool("dimension_walls_by_guides",
    "Create associative aligned wall dimension chains along finite guide elements in a plan. Supports straight vertical basic walls in the active document. Uses the plan cut plane, parallel face references, deterministic minimum-segment filtering and one Undo. Does not dimension door/window jambs, curved/slanted/curtain walls or links. Repeating creates another series. Returns dimension IDs, per-guide failures and warnings. Core mode is experimental.",
    guideDimensionSchema, async args => sendRevitCommand("dimension_walls_by_guides", args));
}
