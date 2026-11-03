import { z } from "zod";
export const idSchema = z.union([z.number().int().safe(), z.string().regex(/^\d+$/)]);
export const pointSchema = z.object({ x: z.number().finite(), y: z.number().finite(), z: z.number().finite() });
export const dimensionSchema = z.object({
  startPoint: pointSchema.describe("Dimension line start, model coordinates in mm"),
  endPoint: pointSchema.describe("Dimension line end, model coordinates in mm"),
  linePoint: pointSchema.optional().describe("Point through which the offset dimension line passes, mm"),
  references: z.array(z.string().min(1)).min(2).optional().describe("Stable Revit reference strings in the active document; takes precedence over elementIds"),
  elementIds: z.array(idSchema).min(2).optional(),
  referenceMode: z.enum(["exterior", "interior", "centerLeftRight", "centerFrontBack", "left", "right"]).default("exterior")
    .describe("Walls: exterior/interior. Families: centerLeftRight/centerFrontBack/left/right. Grids use their axes."),
  dimensionType: z.enum(["Linear", "Aligned"]).default("Linear"),
  dimensionStyleId: idSchema.optional().describe("Linear style ID from get_dimension_types; omit for default"),
  viewId: idSchema.optional().describe("Plan view ID; omit for active view"),
});
export const guideDimensionSchema = {
  guideElementIds: z.array(idSchema).min(1).max(100).describe("Visible straight Model Lines, Detail Lines or Reference Planes; each finite guide defines one chain"),
  wallIds: z.array(idSchema).optional().describe("Optional restriction to these walls; omit for visible walls"),
  viewId: idSchema.optional(),
  dimensionStyleId: idSchema.optional(),
  wallReference: z.enum(["faces", "core"]).default("faces").describe("faces: both finish faces. core: experimental validated internal Revit references; missing core references are never replaced by finish faces"),
  minimumSegmentMm: z.number().finite().nonnegative().default(0).describe("Minimum true segment length in mm; close references are filtered, never text-overridden"),
};
