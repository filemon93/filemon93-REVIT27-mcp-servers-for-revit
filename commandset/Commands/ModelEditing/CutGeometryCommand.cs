using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Cut / uncut / status between cutting elements and targets. Uses an instance void cut when
    /// the cutting element is a family instance with cutting voids, otherwise a solid-solid cut.
    /// </summary>
    public class CutGeometryCommand : DelegateCommandBase
    {
        public override string CommandName => "cut_geometry";

        public CutGeometryCommand(UIApplication uiApp)
            : base(uiApp, "Cut geometry")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var action = (parameters?["action"]?.Value<string>() ?? "cut").ToLowerInvariant();
            if (action != "cut" && action != "uncut" && action != "status")
            {
                throw new ArgumentException("'action' must be cut, uncut or status.");
            }
            var method = (parameters?["method"]?.Value<string>() ?? "auto").ToLowerInvariant();
            if (method != "auto" && method != "solid" && method != "void")
            {
                throw new ArgumentException("'method' must be auto, solid or void.");
            }
            var dryRun = ModelEditingUtils.GetBool(parameters, "dryRun", false) || action == "status";
            var resolveErrors = ModelEditingUtils.GetBool(parameters, "resolveErrors", false);
            var skipBlocked = ModelEditingUtils.GetBool(parameters, "skipBlocked", true);
            var onlyIntersecting = ModelEditingUtils.GetBool(parameters, "onlyIntersecting", true);
            var toleranceMm = ModelEditingUtils.GetDouble(parameters, "toleranceMm", 1);
            var maxPairs = ModelEditingUtils.GetInt(parameters, "maxPairs", 1000);

            return RunInRevit(doc =>
            {
                var toleranceFeet = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);
                var truncated = false;
                List<(Element cutting, Element target)> pairs;
                if (parameters?["pairs"] != null)
                {
                    pairs = ModelEditingUtils.ParsePairs(doc, parameters["pairs"]);
                }
                else
                {
                    var cutting = ModelEditingUtils.SelectElements(doc, parameters?["cutting"]);
                    var targets = ModelEditingUtils.SelectElements(doc, parameters?["targets"]);
                    // Void geometry is not part of an element's visible solids, so void cutters are
                    // matched by bounding box only.
                    var voidCutters = cutting.Where(UsesVoid(method)).ToList();
                    var solidCutters = cutting.Except(voidCutters).ToList();
                    pairs = ModelEditingUtils.FindPairs(doc, solidCutters, targets, toleranceFeet, onlyIntersecting, maxPairs, out truncated);
                    if (voidCutters.Count > 0 && pairs.Count < maxPairs)
                    {
                        pairs.AddRange(ModelEditingUtils.FindPairs(doc, voidCutters, targets, toleranceFeet, false, maxPairs - pairs.Count, out var voidTruncated));
                        truncated |= voidTruncated;
                    }
                }

                var items = new List<Dictionary<string, object>>();
                var planned = new List<(Element cutting, Element target, bool useVoid, Dictionary<string, object> item)>();
                foreach (var (cutting, target) in pairs)
                {
                    var useVoid = UsesVoid(method)(cutting);
                    var exists = useVoid
                        ? InstanceVoidCutUtils.InstanceVoidCutExists(target, cutting)
                        : SolidSolidCutUtils.CutExistsBetweenElements(cutting, target, out _);
                    var item = new Dictionary<string, object>
                    {
                        ["cutting"] = ModelEditingUtils.Brief(doc, cutting),
                        ["target"] = ModelEditingUtils.Brief(doc, target),
                        ["method"] = useVoid ? "void" : "solid",
                        ["cutBefore"] = exists
                    };
                    items.Add(item);

                    if (action == "status")
                    {
                        item["status"] = exists ? "cut" : "not cut";
                        continue;
                    }
                    if (action == "cut")
                    {
                        if (exists)
                        {
                            item["status"] = "unchanged (already cut)";
                            continue;
                        }
                        var reason = CannotCutReason(cutting, target, useVoid);
                        if (reason != null)
                        {
                            item["status"] = $"error: {reason}";
                            continue;
                        }
                    }
                    if (action == "uncut" && !exists)
                    {
                        item["status"] = "unchanged (not cut)";
                        continue;
                    }
                    var blocker = ModelEditingUtils.WorksharingBlocker(doc, cutting.Id) ?? ModelEditingUtils.WorksharingBlocker(doc, target.Id);
                    if (blocker != null && skipBlocked)
                    {
                        item["status"] = $"skipped: {blocker}";
                        continue;
                    }
                    item["status"] = dryRun ? $"would {action}" : "pending";
                    planned.Add((cutting, target, useVoid, item));
                }

                TransactionOutcome outcome = null;
                if (!dryRun && planned.Count > 0)
                {
                    outcome = ModelEditingUtils.RunTransaction(doc, $"MCP: {action} geometry", resolveErrors, () =>
                    {
                        foreach (var (cutting, target, useVoid, item) in planned)
                        {
                            try
                            {
                                if (action == "cut")
                                {
                                    if (useVoid)
                                    {
                                        InstanceVoidCutUtils.AddInstanceVoidCut(doc, target, cutting);
                                    }
                                    else
                                    {
                                        SolidSolidCutUtils.AddCutBetweenSolids(doc, target, cutting);
                                    }
                                    item["status"] = "cut";
                                }
                                else
                                {
                                    if (useVoid)
                                    {
                                        InstanceVoidCutUtils.RemoveInstanceVoidCut(doc, target, cutting);
                                    }
                                    else
                                    {
                                        SolidSolidCutUtils.RemoveCutBetweenSolids(doc, target, cutting);
                                    }
                                    item["status"] = "uncut";
                                }
                            }
                            catch (Exception ex)
                            {
                                item["status"] = $"error: {ex.Message}";
                            }
                        }
                        return true;
                    });

                    if (!outcome.Committed)
                    {
                        foreach (var (_, _, _, item) in planned.Where(p => !((string)p.item["status"]).StartsWith("error")))
                        {
                            item["status"] = "rolled back";
                        }
                    }
                }

                return new
                {
                    action,
                    method,
                    dryRun,
                    transaction = outcome?.Status,
                    pairCount = pairs.Count,
                    pairsTruncatedAtMax = truncated,
                    counts = items.GroupBy(i => ((string)i["status"]).Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()),
                    items = items.Take(300).ToList(),
                    itemsTruncated = items.Count > 300,
                    failures = outcome?.Failures
                };
            });
        }

        private static Func<Element, bool> UsesVoid(string method)
        {
            return element =>
            {
                if (method == "solid")
                {
                    return false;
                }
                var isVoidCutter = element is FamilyInstance && InstanceVoidCutUtils.IsVoidInstanceCuttingElement(element);
                return method == "void" || isVoidCutter;
            };
        }

        private static string CannotCutReason(Element cutting, Element target, bool useVoid)
        {
            if (useVoid)
            {
                if (!InstanceVoidCutUtils.IsVoidInstanceCuttingElement(cutting))
                {
                    return "cutting element has no cutting voids";
                }
                return InstanceVoidCutUtils.CanBeCutWithVoid(target) ? null : "target cannot be cut with a void";
            }
            return SolidSolidCutUtils.CanElementCutElement(cutting, target, out var reason) ? null : $"cannot cut ({reason})";
        }
    }
}
