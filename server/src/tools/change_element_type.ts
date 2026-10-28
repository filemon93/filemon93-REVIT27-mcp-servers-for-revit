import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import {
  editOptionsSchema,
  elementIdsSchema,
  selectorSchema,
  sendRevitCommand,
} from "../utils/modelEditing.js";

export function registerChangeElementTypeTool(server: McpServer) {
  server.tool(
    "change_element_type",
    "Change the type of elements (e.g. swap window types) in one transaction, with a per-element report. Works on elements inside groups (outside Edit Group mode). Use dryRun first to see what would change.",
    {
      changes: z
        .array(
          z.object({
            elementIds: elementIdsSchema.optional().describe("Elements to change"),
            selector: selectorSchema.optional().describe("Alternative to elementIds"),
            typeId: z.union([z.string(), z.number()]).optional().describe("Target type ID"),
            familyName: z.string().optional().describe("Target family name (with typeName)"),
            typeName: z.string().optional().describe("Target type name"),
          })
        )
        .describe("List of changes: which elements get which target type"),
      atomic: z
        .boolean()
        .optional()
        .describe("Roll back everything if any element fails. Default true."),
      ...editOptionsSchema,
    },
    async (args) => sendRevitCommand("change_element_type", args)
  );
}
