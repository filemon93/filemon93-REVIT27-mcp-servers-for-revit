using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Full information about given elements: identity, type, level, group, host, location,
    /// family-instance orientation, worksharing state and parameters.
    /// </summary>
    public class GetElementInfoCommand : DelegateCommandBase
    {
        private const int MaxElements = 200;

        public override string CommandName => "get_element_info";

        public GetElementInfoCommand(UIApplication uiApp)
            : base(uiApp, "Get element info")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var includeParameters = ModelEditingUtils.GetBool(parameters, "includeParameters", true);
            var includeTypeParameters = ModelEditingUtils.GetBool(parameters, "includeTypeParameters", false);
            var parameterNames = ModelEditingUtils.ToStrings(parameters?["parameterNames"]);

            return RunInRevit(doc =>
            {
                var ids = ModelEditingUtils.ParseIds(parameters?["elementIds"]);
                if (ids.Count == 0)
                {
                    throw new ArgumentException("'elementIds' is required.");
                }
                if (ids.Count > MaxElements)
                {
                    throw new ArgumentException($"At most {MaxElements} elements per call.");
                }

                return ids.Select(id =>
                {
                    var e = doc.GetElement(id);
                    if (e == null)
                    {
                        return (object)new { id = id.GetValue(), found = false };
                    }
                    var type = doc.GetElement(e.GetTypeId()) as ElementType;
                    return new
                    {
                        id = id.GetValue(),
                        found = true,
                        uniqueId = e.UniqueId,
                        name = e.Name,
                        category = e.Category?.Name,
                        builtInCategory = e.Category == null ? null : ((BuiltInCategory)(int)e.Category.Id.GetValue()).ToString(),
                        className = e.GetType().Name,
                        familyName = type?.FamilyName,
                        typeName = type?.Name,
                        typeId = type?.Id.GetValue(),
                        level = ModelEditingUtils.GetLevelName(doc, e),
                        groupId = e.GroupId == ElementId.InvalidElementId ? (long?)null : e.GroupId.GetValue(),
                        groupName = ModelEditingUtils.GetGroupName(doc, e),
                        workset = doc.IsWorkshared ? doc.GetWorksetTable().GetWorkset(e.WorksetId)?.Name : null,
                        worksharing = ModelEditingUtils.WorksharingInfo(doc, id),
                        location = DescribeLocation(e.Location),
                        familyInstance = DescribeFamilyInstance(e as FamilyInstance),
                        joinedElements = e is Wall || e is Floor || e is FamilyInstance || e is RoofBase || e is Ceiling
                            ? JoinGeometryUtils.GetJoinedElements(doc, e).Select(j => j.GetValue()).ToList()
                            : null,
                        parameters = includeParameters ? DescribeParameters(e.Parameters, parameterNames) : null,
                        typeParameters = includeTypeParameters && type != null ? DescribeParameters(type.Parameters, parameterNames) : null
                    };
                }).ToList();
            });
        }

        private static object DescribeLocation(Location location)
        {
            switch (location)
            {
                case LocationPoint point:
                    return new
                    {
                        kind = "point",
                        point = ModelEditingUtils.XyzInMillimeters(point.Point),
                        rotationDegrees = SafeRotation(point)
                    };
                case LocationCurve curve:
                    return new
                    {
                        kind = "curve",
                        start = ModelEditingUtils.XyzInMillimeters(curve.Curve.GetEndPoint(0)),
                        end = ModelEditingUtils.XyzInMillimeters(curve.Curve.GetEndPoint(1)),
                        lengthMm = Math.Round(UnitUtils.ConvertFromInternalUnits(curve.Curve.Length, UnitTypeId.Millimeters), 2)
                    };
                default:
                    return null;
            }
        }

        private static double? SafeRotation(LocationPoint point)
        {
            try
            {
                return Math.Round(point.Rotation * 180.0 / Math.PI, 4);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return null;
            }
        }

        private static object DescribeFamilyInstance(FamilyInstance fi)
        {
            if (fi == null)
            {
                return null;
            }
            return new
            {
                hostId = fi.Host?.Id.GetValue(),
                facingFlipped = fi.FacingFlipped,
                handFlipped = fi.HandFlipped,
                mirrored = fi.Mirrored,
                canFlipFacing = fi.CanFlipFacing,
                canFlipHand = fi.CanFlipHand,
                facingOrientation = ModelEditingUtils.Direction(fi.FacingOrientation),
                handOrientation = ModelEditingUtils.Direction(fi.HandOrientation),
                superComponentId = fi.SuperComponent?.Id.GetValue()
            };
        }

        private static List<object> DescribeParameters(ParameterSet parameters, List<string> names)
        {
            return parameters.Cast<Parameter>()
                .Where(p => p.Definition != null && (names.Count == 0 || names.Contains(p.Definition.Name, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(p => p.Definition.Name)
                .Select(ModelEditingUtils.DescribeParameter)
                .ToList();
        }
    }
}
