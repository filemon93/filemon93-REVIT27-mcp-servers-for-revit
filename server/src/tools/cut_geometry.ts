import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import {
  editOptionsSchema,
  elementIdsSchema,
  selectorSchema,
  sendRevitCommand,
} from "../utils/modelEditing.js";

export function registerCutGeometryTool(server: McpServer) {
  server.tool(
    "cut_geometry",
    "Cut or uncut geometry (Revit Cut Geometry), or report cut status. 'auto' uses an instance void cut when the cutting element is a family instance with cutting voids (e.g. opening/void families) and a solid-solid cut otherwise. Give explicit pairs [cuttingId, targetId], or 'cutting' and 'targets' selectors. Use dryRun (or action 'status') to preview.",
    {
      action: z.enum(["cut", "uncut", "status"]).optional().describe("Default 'cut'"),
      method: z
        .enum(["auto", "solid", "void"])
        .optional()
        .describe("Default 'auto'"),
      pairs: z
        .array(elementIdsSchema.length(2))
        .optional()
        .describe("Explicit pairs [cuttingId, targetId]"),
      cutting: selectorSchema.optional().describe("Elements that cut"),
      targets: selectorSchema.optional().describe("Elements to be cut"),
      onlyIntersecting: z
        .boolean()
        .optional()
        .describe("With selectors and solid cuts: pair only intersecting elements. Default true. Void cutters are always matched by bounding box."),
      toleranceMm: z.number().optional().describe("Contact tolerance in mm. Default 1."),
      maxPairs: z.number().optional().describe("Maximum pairs per call. Default 1000."),
      ...editOptionsSchema,
    },
    async (args) => sendRevitCommand("cut_geometry", args)
  );
}
