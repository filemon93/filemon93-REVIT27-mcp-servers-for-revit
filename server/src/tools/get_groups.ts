import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { sendRevitCommand } from "../utils/modelEditing.js";

export function registerGetGroupsTool(server: McpServer) {
  server.tool(
    "get_groups",
    "List group types and their instances: level, origin (mm), nesting, member counts by category, optional member IDs, and worksharing state per instance (owner, or 'not latest' when Reload Latest is needed before editing).",
    {
      nameContains: z
        .string()
        .optional()
        .describe("Only group types whose name contains this text"),
      includeDetailGroups: z
        .boolean()
        .optional()
        .describe("Include detail groups. Default false (model groups only)."),
      memberCategory: z
        .string()
        .optional()
        .describe("Count members of this category per instance (e.g. 'OST_Windows')"),
      onlyWithMemberCategory: z
        .boolean()
        .optional()
        .describe("With memberCategory: skip group types that have no such members"),
      includeMemberIds: z
        .boolean()
        .optional()
        .describe("Return member IDs per instance (filtered by memberCategory if given). Default false."),
    },
    async (args) => sendRevitCommand("get_groups", args)
  );
}
