using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Join / unjoin / switch join order / status for pairs of elements. Pairs come from an explicit
    /// list or from two selectors, keeping only elements that touch within a tolerance.
    /// </summary>
    public class JoinGeometryCommand : DelegateCommandBase
    {
        public override string CommandName => "join_geometry";

        public JoinGeometryCommand(UIApplication uiApp)
            : base(uiApp, "Join geometry")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var action = (parameters?["action"]?.Value<string>() ?? "join").ToLowerInvariant();
            if (action != "join" && action != "unjoin" && action != "switch_order" && action != "status")
            {
                throw new ArgumentException("'action' must be join, unjoin, switch_order or status.");
            }
            var dryRun = ModelEditingUtils.GetBool(parameters, "dryRun", false) || action == "status";
            var resolveErrors = ModelEditingUtils.GetBool(parameters, "resolveErrors", false);
            var skipBlocked = ModelEditingUtils.GetBool(parameters, "skipBlocked", true);
            var onlyTouching = ModelEditingUtils.GetBool(parameters, "onlyTouching", true);
            var toleranceMm = ModelEditingUtils.GetDouble(parameters, "toleranceMm", 5);
            var maxPairs = ModelEditingUtils.GetInt(parameters, "maxPairs", 1000);

            return RunInRevit(doc =>
            {
                var toleranceFeet = UnitUtils.ConvertToInternalUnits(toleranceMm, UnitTypeId.Millimeters);
                var truncated = false;
                var pairs = parameters?["pairs"] != null
                    ? ModelEditingUtils.ParsePairs(doc, parameters["pairs"])
                    : ModelEditingUtils.FindPairs(
                        doc,
                        ModelEditingUtils.SelectElements(doc, parameters?["first"]),
                        ModelEditingUtils.SelectElements(doc, parameters?["second"]),
                        toleranceFeet, onlyTouching, maxPairs, out truncated);

                var items = new List<Dictionary<string, object>>();
                var planned = new List<(Element a, Element b, Dictionary<string, object> item)>();
                foreach (var (a, b) in pairs)
                {
                    var joined = JoinGeometryUtils.AreElementsJoined(doc, a, b);
                    var item = new Dictionary<string, object>
                    {
                        ["first"] = ModelEditingUtils.Brief(doc, a),
                        ["second"] = ModelEditingUtils.Brief(doc, b),
                        ["joinedBefore"] = joined
                    };
                    if (joined)
                    {
                        item["firstCutsSecond"] = JoinGeometryUtils.IsCuttingElementInJoin(doc, a, b);
                    }
                    items.Add(item);

                    if (action == "status")
                    {
                        item["status"] = joined ? "joined" : "not joined";
                        continue;
                    }
                    if (action == "join" && joined)
                    {
                        item["status"] = "unchanged (already joined)";
                        continue;
                    }
                    if ((action == "unjoin" || action == "switch_order") && !joined)
                    {
                        item["status"] = "unchanged (not joined)";
                        continue;
                    }
                    var blocker = ModelEditingUtils.WorksharingBlocker(doc, a.Id) ?? ModelEditingUtils.WorksharingBlocker(doc, b.Id);
                    if (blocker != null && skipBlocked)
                    {
                        item["status"] = $"skipped: {blocker}";
                        continue;
                    }
                    item["status"] = dryRun ? $"would {action}" : "pending";
                    planned.Add((a, b, item));
                }

                TransactionOutcome outcome = null;
                if (!dryRun && planned.Count > 0)
                {
                    outcome = ModelEditingUtils.RunTransaction(doc, $"MCP: {action} geometry", resolveErrors, () =>
                    {
                        foreach (var (a, b, item) in planned)
                        {
                            try
                            {
                                switch (action)
                                {
                                    case "join":
                                        JoinGeometryUtils.JoinGeometry(doc, a, b);
                                        item["status"] = "joined";
                                        break;
                                    case "unjoin":
                                        JoinGeometryUtils.UnjoinGeometry(doc, a, b);
                                        item["status"] = "unjoined";
                                        break;
                                    default:
                                        JoinGeometryUtils.SwitchJoinOrder(doc, a, b);
                                        item["status"] = "order switched";
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // A rejected pair does not stop the others.
                                item["status"] = $"error: {ex.Message}";
                            }
                        }
                        return true;
                    });

                    if (!outcome.Committed)
                    {
                        foreach (var (_, _, item) in planned.Where(p => !((string)p.item["status"]).StartsWith("error")))
                        {
                            item["status"] = "rolled back";
                        }
                    }
                }

                return new
                {
                    action,
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
    }
}
