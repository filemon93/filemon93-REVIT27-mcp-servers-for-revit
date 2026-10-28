import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import {
  editOptionsSchema,
  elementIdsSchema,
  selectorSchema,
  sendRevitCommand,
} from "../utils/modelEditing.js";

export function registerJoinGeometryTool(server: McpServer) {
  server.tool(
    "join_geometry",
    "Join, unjoin or switch join order of element geometry (Revit Join Geometry), or report join status. Give explicit pairs, or two selectors ('first' and 'second'); with selectors only elements that touch or overlap within toleranceMm are paired. Use dryRun (or action 'status') to preview. Large sets: limit with maxPairs and repeat.",
    {
      action: z
        .enum(["join", "unjoin", "switch_order", "status"])
        .optional()
        .describe("Default 'join'"),
      pairs: z
        .array(elementIdsSchema.length(2))
        .optional()
        .describe("Explicit pairs [firstId, secondId]"),
      first: selectorSchema.optional().describe("First element set, e.g. exterior walls"),
      second: selectorSchema.optional().describe("Second element set, e.g. structural walls and plaster"),
      onlyTouching: z
        .boolean()
        .optional()
        .describe("With selectors: pair only elements whose geometry touches or overlaps. Default true; false uses bounding boxes only."),
      toleranceMm: z
        .number()
        .optional()
        .describe("Contact tolerance in mm. Default 5."),
      maxPairs: z.number().optional().describe("Maximum pairs per call. Default 1000."),
      ...editOptionsSchema,
    },
    async (args) => sendRevitCommand("join_geometry", args)
  );
}
