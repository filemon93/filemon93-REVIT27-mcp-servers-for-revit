using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.AnnotationComponents;

public static class DimensionService
{
    public static ViewPlan Plan(Document doc, long id)
    {
        var view = id > 0 ? doc.GetElement(ModelEditingUtils.ToElementId(id)) as View : doc.ActiveView;
        if (view is not ViewPlan plan || view.IsTemplate ||
            (view.ViewType != ViewType.FloorPlan && view.ViewType != ViewType.CeilingPlan && view.ViewType != ViewType.EngineeringPlan))
            throw new ArgumentException("Choose a non-template floor, ceiling or structural plan view.");
        return plan;
    }

    public static DimensionType Style(Document doc, long id)
    {
        var style = id > 0 ? doc.GetElement(ModelEditingUtils.ToElementId(id)) as DimensionType
            : new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(t => t.StyleType == DimensionStyleType.Linear);
        if (style == null || style.StyleType != DimensionStyleType.Linear)
            throw new ArgumentException("A linear dimension style is required. Use get_dimension_types.");
        return style;
    }

    private static XYZ Point(JZPoint point)
    {
        if (point == null || new[] { point.X, point.Y, point.Z }.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            throw new ArgumentException("Provide finite point coordinates in millimeters.");
        return new XYZ(point.X / 304.8, point.Y / 304.8, point.Z / 304.8);
    }

    public static object Create(Document doc, List<DimensionCreationInfo> items)
    {
        var results = new List<object>();
        var created = 0;
        using var group = new TransactionGroup(doc, "MCP dimensions");
        group.Start();
        for (var i = 0; i < items.Count; i++)
        {
            using var tx = new Transaction(doc, "MCP dimension " + i);
            try
            {
                var item = items[i];
                var view = Plan(doc, item.ViewId);
                var style = Style(doc, item.DimensionStyleId);
                if (item.DimensionType != "Linear" && item.DimensionType != "Aligned")
                    throw new ArgumentException("Only Linear/Aligned dimensions are supported.");
                if (item.Options?.Count > 0)
                    throw new ArgumentException("Arbitrary parameter overrides are not supported.");
                XYZ Project(XYZ p) => p - view.ViewDirection * (p - view.Origin).DotProduct(view.ViewDirection);
                var a = Project(Point(item.StartPoint));
                var b = Project(Point(item.EndPoint));
                if (a.DistanceTo(b) < doc.Application.ShortCurveTolerance)
                    throw new ArgumentException("Dimension line is too short in the view plane.");
                var direction = (b - a).Normalize();
                if (item.LinePoint != null)
                {
                    var delta = Project(Point(item.LinePoint)) - a;
                    var offset = delta - direction * delta.DotProduct(direction);
                    a += offset; b += offset;
                }
                var refs = new ReferenceArray();
                if (item.References?.Count >= 2)
                {
                    foreach (var stable in item.References.Distinct())
                    {
                        var reference = Reference.ParseFromStableRepresentation(doc, stable);
                        if (reference.LinkedElementId != ElementId.InvalidElementId)
                            throw new ArgumentException("Linked references are not supported in this release.");
                        refs.Append(reference);
                    }
                }
                else if (item.ElementIds?.Count >= 2)
                {
                    var visible = new FilteredElementCollector(doc, view.Id).ToElementIds().ToHashSet();
                    foreach (var id in item.ElementIds.Distinct())
                    {
                        var element = doc.GetElement(ModelEditingUtils.ToElementId(id));
                        if (element == null || !visible.Contains(element.Id) || element.IsHidden(view))
                            throw new ArgumentException($"Element {id} is missing or not visible in the plan.");
                        refs.Append(ElementReference(element, direction, item.ReferenceMode));
                    }
                }
                else throw new ArgumentException("Provide at least two stable references or elementIds; point-only reference guessing is disabled.");
                if (refs.Size < 2) throw new ArgumentException("At least two distinct references are required.");
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RejectFailures()).SetClearAfterRollback(true));
                var dimension = doc.Create.NewDimension(view, Line.CreateBound(a, b), refs, style)
                    ?? throw new InvalidOperationException("Revit did not create a dimension.");
                doc.Regenerate();
                var values = dimension.NumberOfSegments == 0 ? new[] { dimension.Value ?? double.NaN }
                    : dimension.Segments.Cast<DimensionSegment>().Select(s => s.Value ?? double.NaN).ToArray();
                if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v <= 1e-7))
                    throw new InvalidOperationException("Invalid or zero-length dimension segments.");
                if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit rejected the dimension.");
                created++;
                results.Add(new { index = i, success = true, dimensionId = dimension.Id.ToString(), segmentLengthsMm = values.Select(v => v * 304.8).ToArray() });
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                group.RollBack();
                throw;
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                results.Add(new { index = i, success = false, error = ex.Message });
            }
        }
        if (created > 0) group.Assimilate(); else group.RollBack();
        return new { success = created == items.Count, created, failed = items.Count - created, results };
    }

    private static Reference ElementReference(Element element, XYZ direction, string mode)
    {
        if (element is Wall wall)
        {
            if (mode != "exterior" && mode != "interior")
                throw new ArgumentException("For walls use referenceMode exterior/interior, or supply stable references.");
            var faces = HostObjectUtils.GetSideFaces(wall, mode == "exterior" ? ShellLayerType.Exterior : ShellLayerType.Interior)
                .Where(r => wall.GetGeometryObjectFromReference(r) is PlanarFace f && Math.Abs(f.FaceNormal.DotProduct(direction)) > 1 - 1e-6).ToList();
            if (faces.Count != 1) throw new ArgumentException($"Wall {wall.Id} has no unambiguous perpendicular {mode} face.");
            return faces[0];
        }
        if (element is FamilyInstance family)
        {
            var type = mode switch
            {
                "centerLeftRight" => FamilyInstanceReferenceType.CenterLeftRight,
                "centerFrontBack" => FamilyInstanceReferenceType.CenterFrontBack,
                "left" => FamilyInstanceReferenceType.Left,
                "right" => FamilyInstanceReferenceType.Right,
                _ => throw new ArgumentException("For families choose centerLeftRight, centerFrontBack, left or right.")
            };
            var references = family.GetReferences(type);
            if (references?.Count != 1) throw new ArgumentException($"Family {family.Id} does not expose one {mode} reference.");
            return references[0];
        }
        if (element is Grid grid && grid.Curve is Line line && Math.Abs(line.Direction.DotProduct(direction)) < 1e-6)
            return new Reference(grid);
        if (element is ReferencePlane plane && Math.Abs(plane.GetPlane().Normal.DotProduct(direction)) > 1 - 1e-6)
            return plane.GetReference();
        throw new ArgumentException($"Element {element.Id} needs explicit stable references or is incompatible with the dimension direction.");
    }

    private sealed class RejectFailures : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor) =>
            accessor.GetFailureMessages().Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
