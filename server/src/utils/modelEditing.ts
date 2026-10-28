import { z } from "zod";
import { withRevitConnection } from "./ConnectionManager.js";

export const elementIdsSchema = z
  .array(z.union([z.string(), z.number()]))
  .describe("Revit element IDs");

export const selectorSchema = z
  .object({
    elementIds: elementIdsSchema
      .optional()
      .describe("Explicit element IDs. The name filters below still narrow them."),
    category: z
      .string()
      .optional()
      .describe("Category as BuiltInCategory (e.g. 'OST_Walls') or display name. Required when elementIds is not given."),
    typeNames: z.array(z.string()).optional().describe("Exact type names to keep"),
    typeNameContains: z
      .array(z.string())
      .optional()
      .describe("Keep elements whose type name contains any of these (case-insensitive)"),
    familyNameContains: z
      .array(z.string())
      .optional()
      .describe("Keep elements whose family name contains any of these (case-insensitive)"),
    levelNames: z
      .array(z.string())
      .optional()
      .describe("Keep elements on these levels (exact level names, e.g. 'poziom 03')"),
  })
  .describe("Selects element instances by IDs or by category and name filters");

export const editOptionsSchema = {
  dryRun: z
    .boolean()
    .optional()
    .describe("Only report what would change; nothing is modified. Default false."),
  resolveErrors: z
    .boolean()
    .optional()
    .describe(
      "Apply Revit's default resolution to errors (e.g. delete invalid dimensions, unjoin elements). Default false: any error rolls back the whole transaction."
    ),
  skipBlocked: z
    .boolean()
    .optional()
    .describe(
      "Skip elements owned by another user or not up to date with central (Reload Latest needed). Default true."
    ),
};

export async function sendRevitCommand(command: string, params: object) {
  try {
    const response = await withRevitConnection(async (revitClient) => {
      return await revitClient.sendCommand(command, params);
    });
    return {
      content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }],
    };
  } catch (error) {
    return {
      content: [
        {
          type: "text" as const,
          text: `${command} failed: ${error instanceof Error ? error.message : String(error)}`,
        },
      ],
    };
  }
}
