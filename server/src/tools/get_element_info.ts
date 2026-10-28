import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { elementIdsSchema, sendRevitCommand } from "../utils/modelEditing.js";

export function registerGetElementInfoTool(server: McpServer) {
  server.tool(
    "get_element_info",
    "Get full information about elements by ID: category, family and type, level, group, workset, worksharing state (owner, whether Reload Latest is needed), location in mm, family-instance orientation (facing/hand flipped, mirrored, host), joined elements and parameters with values. Up to 200 elements per call.",
    {
      elementIds: elementIdsSchema.describe("IDs of the elements to inspect (max 200)"),
      includeParameters: z
        .boolean()
        .optional()
        .describe("Include instance parameters. Default true."),
      includeTypeParameters: z
        .boolean()
        .optional()
        .describe("Include type parameters. Default false."),
      parameterNames: z
        .array(z.string())
        .optional()
        .describe("Only return these parameters (by name)"),
    },
    async (args) => sendRevitCommand("get_element_info", args)
  );
}
