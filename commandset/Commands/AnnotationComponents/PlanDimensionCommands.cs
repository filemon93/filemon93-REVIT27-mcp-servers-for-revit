using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Commands.ModelEditing;
using RevitMCPCommandSet.Services.AnnotationComponents;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.AnnotationComponents;

public class GetDimensionTypesCommand(UIApplication app) : DelegateCommandBase(app, "Get dimension types")
{
    public override string CommandName => "get_dimension_types";
    public override object Execute(JObject parameters, string requestId) => RunInRevit(doc =>
        new { success = true, types = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
            .Where(t => t.StyleType == DimensionStyleType.Linear).OrderBy(t => t.Name)
            .Select(t => new { id = t.Id.ToString(), name = t.Name, familyName = t.FamilyName }).ToArray() });
}

public class DimensionWallsByGuidesCommand(UIApplication app) : DelegateCommandBase(app, "Dimension walls by guides")
{
    public override string CommandName => "dimension_walls_by_guides";
    public override object Execute(JObject parameters, string requestId)
    {
        var guides = ModelEditingUtils.ParseIds(parameters["guideElementIds"]).Distinct().ToList();
        var walls = parameters["wallIds"] == null ? null : ModelEditingUtils.ParseIds(parameters["wallIds"]).ToHashSet();
        var minimum = parameters.Value<double?>("minimumSegmentMm") ?? 0;
        var core = parameters.Value<string>("wallReference") ?? "faces";
        if (guides.Count == 0 || guides.Count > 100) throw new ArgumentException("Provide 1-100 guideElementIds.");
        if (double.IsNaN(minimum) || double.IsInfinity(minimum) || minimum < 0) throw new ArgumentException("minimumSegmentMm must be finite and nonnegative.");
        if (core != "faces" && core != "core") throw new ArgumentException("wallReference must be faces or core.");
        return RunInRevit(doc =>
        {
            var view = DimensionService.Plan(doc, parameters.Value<long?>("viewId") ?? -1);
            var type = DimensionService.Style(doc, parameters.Value<long?>("dimensionStyleId") ?? -1);
            var visible = new FilteredElementCollector(doc, view.Id).ToElementIds().ToHashSet();
            foreach (var id in guides)
                if (!visible.Contains(id) || doc.GetElement(id).IsHidden(view)) throw new ArgumentException($"Guide {id} is not visible in the selected view.");
            if (walls != null && walls.Any(id => doc.GetElement(id) is not Wall)) throw new ArgumentException("wallIds must contain existing walls in the active document.");
            return new DimensionEngine(doc, view).Run(guides, type, core == "core", minimum / 304.8, walls);
        });
    }
}
