using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Changes the type of elements in one transaction. Each change targets elements (ids or a
    /// selector) and a type (id, or family + type name). Supports dry run; per-element results.
    /// </summary>
    public class ChangeElementTypeCommand : DelegateCommandBase
    {
        public override string CommandName => "change_element_type";

        public ChangeElementTypeCommand(UIApplication uiApp)
            : base(uiApp, "Change element type")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var dryRun = ModelEditingUtils.GetBool(parameters, "dryRun", false);
            var resolveErrors = ModelEditingUtils.GetBool(parameters, "resolveErrors", false);
            var skipBlocked = ModelEditingUtils.GetBool(parameters, "skipBlocked", true);
            var atomic = ModelEditingUtils.GetBool(parameters, "atomic", true);
            var changes = parameters?["changes"] as JArray
                          ?? throw new ArgumentException("'changes' is required.");

            return RunInRevit(doc =>
            {
                var planned = new List<(Element element, ElementType target, Dictionary<string, object> item)>();
                var items = new List<Dictionary<string, object>>();

                foreach (JObject change in changes)
                {
                    var target = ResolveType(doc, change);
                    var elements = change["selector"] != null
                        ? ModelEditingUtils.SelectElements(doc, change["selector"])
                        : ModelEditingUtils.ParseIds(change["elementIds"]).Select(doc.GetElement).Where(e => e != null).ToList();

                    foreach (var element in elements)
                    {
                        var item = new Dictionary<string, object>
                        {
                            ["id"] = element.Id.GetValue(),
                            ["from"] = $"{ModelEditingUtils.GetFamilyName(doc, element)} : {ModelEditingUtils.GetTypeName(doc, element)}",
                            ["to"] = $"{target.FamilyName} : {target.Name}",
                            ["group"] = ModelEditingUtils.GetGroupName(doc, element)
                        };
                        items.Add(item);

                        if (element.GetTypeId() == target.Id)
                        {
                            item["status"] = "unchanged (already this type)";
                            continue;
                        }
                        if (!element.GetValidTypes().Contains(target.Id))
                        {
                            item["status"] = "error: target type is not valid for this element";
                            continue;
                        }
                        var blocker = ModelEditingUtils.WorksharingBlocker(doc, element.Id);
                        if (blocker != null && skipBlocked)
                        {
                            item["status"] = $"skipped: {blocker}";
                            continue;
                        }
                        item["status"] = dryRun ? "would change" : "pending";
                        planned.Add((element, target, item));
                    }
                }

                if (dryRun || planned.Count == 0)
                {
                    return Summary(dryRun, null, items, null);
                }

                var outcome = ModelEditingUtils.RunTransaction(doc, "MCP: change element type", resolveErrors, () =>
                {
                    var anyError = false;
                    foreach (var (element, target, item) in planned)
                    {
                        try
                        {
                            element.ChangeTypeId(target.Id);
                            item["status"] = "changed";
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
                    foreach (var item in items.Where(i => (string)i["status"] == "changed"))
                    {
                        item["status"] = "rolled back";
                    }
                }
                return Summary(false, outcome, items, outcome.Failures);
            });
        }

        private static ElementType ResolveType(Document doc, JObject change)
        {
            var typeIdToken = change["typeId"];
            if (typeIdToken != null && typeIdToken.Type != JTokenType.Null)
            {
                var id = ModelEditingUtils.ToElementId(long.Parse(typeIdToken.ToString()));
                return doc.GetElement(id) as ElementType
                       ?? throw new ArgumentException($"Type {typeIdToken} not found.");
            }

            var familyName = change["familyName"]?.Value<string>();
            var typeName = change["typeName"]?.Value<string>()
                           ?? throw new ArgumentException("Each change needs 'typeId' or 'typeName' (optionally with 'familyName').");
            var matches = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(t => t.Name == typeName && (familyName == null || t.FamilyName == familyName))
                .ToList();
            if (matches.Count == 0)
            {
                throw new ArgumentException($"Type not found: {familyName} : {typeName}");
            }
            if (matches.Count > 1)
            {
                throw new ArgumentException(
                    $"Type name '{typeName}' is ambiguous ({string.Join(", ", matches.Select(m => m.FamilyName))}); add 'familyName' or use 'typeId'.");
            }
            return matches[0];
        }

        private static object Summary(bool dryRun, TransactionOutcome outcome, List<Dictionary<string, object>> items, List<object> failures)
        {
            return new
            {
                dryRun,
                transaction = outcome?.Status,
                counts = items.GroupBy(i => ((string)i["status"]).Split(':')[0]).ToDictionary(g => g.Key, g => g.Count()),
                items = items.Take(500).ToList(),
                itemsTruncated = items.Count > 500,
                failures
            };
        }
    }
}
