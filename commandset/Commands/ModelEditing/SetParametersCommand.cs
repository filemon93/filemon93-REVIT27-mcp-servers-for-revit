using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Sets parameter values on elements or on their types, in one transaction.
    /// Numbers for length/area/etc. are read in project display units unless units = "internal";
    /// strings on numeric parameters go through SetValueString (e.g. "27 cm").
    /// </summary>
    public class SetParametersCommand : DelegateCommandBase
    {
        public override string CommandName => "set_parameters";

        public SetParametersCommand(UIApplication uiApp)
            : base(uiApp, "Set parameters")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var dryRun = ModelEditingUtils.GetBool(parameters, "dryRun", false);
            var resolveErrors = ModelEditingUtils.GetBool(parameters, "resolveErrors", false);
            var skipBlocked = ModelEditingUtils.GetBool(parameters, "skipBlocked", true);
            var atomic = ModelEditingUtils.GetBool(parameters, "atomic", true);
            var assignments = parameters?["items"] as JArray
                              ?? throw new ArgumentException("'items' is required.");

            return RunInRevit(doc =>
            {
                var planned = new List<(Parameter parameter, JToken value, bool internalUnits, Dictionary<string, object> item)>();
                var items = new List<Dictionary<string, object>>();

                foreach (JObject assignment in assignments)
                {
                    var parameterName = assignment["parameterName"]?.Value<string>();
                    var builtIn = assignment["builtInParameter"]?.Value<string>();
                    if (string.IsNullOrEmpty(parameterName) && string.IsNullOrEmpty(builtIn))
                    {
                        throw new ArgumentException("Each item needs 'parameterName' or 'builtInParameter'.");
                    }
                    var value = assignment["value"] ?? throw new ArgumentException("Each item needs 'value'.");
                    var onType = string.Equals(assignment["target"]?.Value<string>(), "type", StringComparison.OrdinalIgnoreCase);
                    var internalUnits = string.Equals(assignment["units"]?.Value<string>(), "internal", StringComparison.OrdinalIgnoreCase);

                    var elements = assignment["selector"] != null
                        ? ModelEditingUtils.SelectElements(doc, assignment["selector"])
                        : ModelEditingUtils.ParseIds(assignment["elementIds"]).Select(doc.GetElement).Where(e => e != null).ToList();
                    var owners = onType
                        ? elements.Select(e => doc.GetElement(e.GetTypeId())).Where(t => t != null).GroupBy(t => t.Id).Select(g => g.First()).ToList()
                        : elements;

                    foreach (var owner in owners)
                    {
                        var item = new Dictionary<string, object>
                        {
                            ["id"] = owner.Id.GetValue(),
                            ["target"] = onType ? "type" : "instance",
                            ["name"] = owner.Name,
                            ["parameter"] = parameterName ?? builtIn
                        };
                        items.Add(item);

                        var parameter = string.IsNullOrEmpty(builtIn)
                            ? owner.LookupParameter(parameterName)
                            : owner.get_Parameter((BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtIn, true));
                        if (parameter == null)
                        {
                            item["status"] = "error: parameter not found";
                            continue;
                        }
                        item["before"] = ModelEditingUtils.DisplayValue(parameter);
                        if (parameter.IsReadOnly)
                        {
                            item["status"] = "error: parameter is read-only";
                            continue;
                        }
                        var blocker = ModelEditingUtils.WorksharingBlocker(doc, owner.Id);
                        if (blocker != null && skipBlocked)
                        {
                            item["status"] = $"skipped: {blocker}";
                            continue;
                        }
                        item["status"] = dryRun ? "would set" : "pending";
                        planned.Add((parameter, value, internalUnits, item));
                    }
                }

                if (dryRun || planned.Count == 0)
                {
                    return Summary(dryRun, null, items);
                }

                var outcome = ModelEditingUtils.RunTransaction(doc, "MCP: set parameters", resolveErrors, () =>
                {
                    var anyError = false;
                    foreach (var (parameter, value, internalUnits, item) in planned)
                    {
                        try
                        {
                            SetValue(doc, parameter, value, internalUnits);
                            item["status"] = "set";
                            item["after"] = ModelEditingUtils.DisplayValue(parameter);
                        }
                        catch (Exception ex)
                        {
                            item["status"] = $"error: {ex.Message}";
                            anyError = true;
                        }
                    }
                    return !(atomic && anyError);
                });

                if (!outcome.Committed)
                {
                    foreach (var item in items.Where(i => (string)i["status"] == "set"))
                    {
                        item["status"] = "rolled back";
                    }
                }
                return Summary(false, outcome, items);
            });
        }

        private static void SetValue(Document doc, Parameter parameter, JToken value, bool internalUnits)
        {
            bool ok;
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    ok = parameter.Set(value.ToString());
                    break;
                case StorageType.Integer:
                    ok = parameter.Set(ToInteger(value));
                    break;
                case StorageType.ElementId:
                    ok = parameter.Set(ModelEditingUtils.ToElementId(long.Parse(value.ToString())));
                    break;
                case StorageType.Double:
                    if (value.Type == JTokenType.String)
                    {
                        ok = parameter.SetValueString(value.ToString());
                    }
                    else
                    {
                        var number = value.Value<double>();
                        ok = parameter.Set(internalUnits ? number : ToInternal(doc, parameter, number));
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported storage type {parameter.StorageType}.");
            }
            if (!ok)
            {
                throw new InvalidOperationException($"Revit rejected value '{value}'.");
            }
        }

        private static int ToInteger(JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Boolean:
                    return value.Value<bool>() ? 1 : 0;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return (int)Math.Round(value.Value<double>());
                default:
                    var text = value.ToString().Trim().ToLowerInvariant();
                    if (text == "true" || text == "yes" || text == "tak")
                    {
                        return 1;
                    }
                    if (text == "false" || text == "no" || text == "nie")
                    {
                        return 0;
                    }
                    return int.Parse(text);
            }
        }

        private static double ToInternal(Document doc, Parameter parameter, double number)
        {
#if REVIT2022_OR_GREATER
            var spec = parameter.Definition.GetDataType();
            if (UnitUtils.IsMeasurableSpec(spec))
            {
                var unit = doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId();
                return UnitUtils.ConvertToInternalUnits(number, unit);
            }
#endif
            return number;
        }

        private static object Summary(bool dryRun, TransactionOutcome outcome, List<Dictionary<string, object>> items)
        {
            return new
            {
                dryRun,
                transaction = outcome?.Status,
                counts = items.GroupBy(i => ((string)i["status"]).Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()),
                items = items.Take(500).ToList(),
                itemsTruncated = items.Count > 500,
                failures = outcome?.Failures
            };
        }
    }
}
