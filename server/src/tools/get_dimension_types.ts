import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { sendRevitCommand } from "../utils/modelEditing.js";
export function registerGetDimensionTypesTool(server: McpServer) {
  server.tool("get_dimension_types", "List linear dimension style IDs and names from the active Revit document.", {},
    async () => sendRevitCommand("get_dimension_types", {}));
}
