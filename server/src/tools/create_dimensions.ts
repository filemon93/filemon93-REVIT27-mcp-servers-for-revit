import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { sendRevitCommand } from "../utils/modelEditing.js";
import { dimensionSchema } from "../utils/dimensionSchemas.js";
export function registerCreateDimensionsTool(server: McpServer) {
  server.tool("create_dimensions",
    "Create associative linear dimensions in a floor/ceiling/structural plan. Supply stable references OR elementIds with explicit referenceMode. Points define the line; linePoint offsets it. No point-only reference guessing. Coordinates are mm. Returns per-item results and actual segment lengths; partial success is possible. One Undo for the batch.",
    { dimensions: z.array(dimensionSchema).min(1).max(100) },
    async args => sendRevitCommand("create_dimensions", args));
}
