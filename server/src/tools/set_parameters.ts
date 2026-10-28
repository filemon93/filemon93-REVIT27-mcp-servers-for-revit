import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import {
  editOptionsSchema,
  elementIdsSchema,
  selectorSchema,
  sendRevitCommand,
} from "../utils/modelEditing.js";

export function registerSetParametersTool(server: McpServer) {
  server.tool(
    "set_parameters",
    "Set parameter values on elements or their types in one transaction, with before/after values. Numbers for lengths, areas etc. are in project display units (e.g. cm) unless units='internal'; a string value on a numeric parameter is parsed like Revit's UI (e.g. '27 cm'). Yes/No parameters accept true/false.",
    {
      items: z
        .array(
          z.object({
            elementIds: elementIdsSchema.optional().describe("Elements to modify"),
            selector: selectorSchema.optional().describe("Alternative to elementIds"),
            parameterName: z.string().optional().describe("Parameter name as shown in Properties"),
            builtInParameter: z
              .string()
              .optional()
              .describe("BuiltInParameter name instead of parameterName (e.g. 'ALL_MODEL_MARK')"),
            value: z
              .union([z.string(), z.number(), z.boolean()])
              .describe("New value"),
            target: z
              .enum(["instance", "type"])
              .optional()
              .describe("Set on the element (default) or on its type"),
            units: z
              .enum(["project", "internal"])
              .optional()
              .describe("Units of numeric values: project display units (default) or Revit internal units (feet)"),
          })
        )
        .describe("Parameter assignments"),
      atomic: z
        .boolean()
        .optional()
        .describe("Roll back everything if any assignment fails. Default true."),
      ...editOptionsSchema,
    },
    async (args) => sendRevitCommand("set_parameters", args)
  );
}
