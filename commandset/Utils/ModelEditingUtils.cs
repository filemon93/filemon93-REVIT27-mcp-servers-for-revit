using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Shared helpers for the model-editing tools: id parsing, element selectors,
    /// worksharing checks, parameter formatting and failure-collecting transactions.
    /// </summary>
    public static class ModelEditingUtils
    {
        public static ElementId ToElementId(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        public static List<ElementId> ParseIds(JToken token)
        {
            var ids = new List<ElementId>();
            if (token == null || token.Type == JTokenType.Null)
            {
                return ids;
            }
            foreach (var item in token)
            {
                if (!long.TryParse(item.ToString(), out var value))
                {
                    throw new ArgumentException($"Invalid element id: {item}");
                }
                ids.Add(ToElementId(value));
            }
            return ids;
        }

        public static List<string> ToStrings(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string>();
            }
            if (token.Type == JTokenType.Array)
            {
                return token.Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
            return new List<string> { token.ToString() };
        }

        public static bool GetBool(JObject parameters, string name, bool defaultValue)
        {
            var token = parameters?[name];
            return token == null || token.Type == JTokenType.Null ? defaultValue : token.Value<bool>();
        }

        public static int GetInt(JObject parameters, string name, int defaultValue)
        {
            var token = parameters?[name];
            return token == null || token.Type == JTokenType.Null ? defaultValue : token.Value<int>();
        }

        public static double GetDouble(JObject parameters, string name, double defaultValue)
        {
            var token = parameters?[name];
            return token == null || token.Type == JTokenType.Null ? defaultValue : token.Value<double>();
        }

        /// <summary>
        /// Resolves a category given as "OST_Walls" or by its display name ("Walls", "Ściany").
        /// </summary>
        public static ElementId ParseCategoryId(Document doc, string category)
        {
            if (Enum.TryParse(category, true, out BuiltInCategory bic))
            {
                return ToElementId((long)bic);
            }
            foreach (Category cat in doc.Settings.Categories)
            {
                if (string.Equals(cat.Name, category, StringComparison.OrdinalIgnoreCase))
                {
                    return cat.Id;
                }
            }
            throw new ArgumentException($"Unknown category: {category}");
        }

        /// <summary>
        /// Selects element instances. A selector is either { elementIds: [...] } or
        /// { category, typeNames, typeNameContains, familyNameContains, levelNames }; the
        /// name filters also narrow an explicit id list.
        /// </summary>
        public static List<Element> SelectElements(Document doc, JToken selectorToken)
        {
            var selector = selectorToken as JObject;
            if (selector == null)
            {
                return new List<Element>();
            }

            var explicitIds = ParseIds(selector["elementIds"]);
            IEnumerable<Element> elements;
            if (explicitIds.Count > 0)
            {
                elements = explicitIds.Select(doc.GetElement).Where(e => e != null);
            }
            else
            {
                var category = selector["category"]?.Value<string>();
                if (string.IsNullOrEmpty(category))
                {
                    throw new ArgumentException("A selector needs either 'elementIds' or 'category'.");
                }
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementCategoryFilter(ParseCategoryId(doc, category)))
                    .ToElements();
            }

            var typeNames = ToStrings(selector["typeNames"]);
            var typeContains = ToStrings(selector["typeNameContains"]);
            var familyContains = ToStrings(selector["familyNameContains"]);
            var levelNames = ToStrings(selector["levelNames"]);

            return elements.Where(e =>
            {
                var typeName = GetTypeName(doc, e) ?? string.Empty;
                if (typeNames.Count > 0 && !typeNames.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (typeContains.Count > 0 && !typeContains.Any(s => typeName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return false;
                }
                if (familyContains.Count > 0)
                {
                    var family = GetFamilyName(doc, e) ?? string.Empty;
                    if (!familyContains.Any(s => family.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return false;
                    }
                }
                if (levelNames.Count > 0)
                {
                    var level = GetLevelName(doc, e);
                    if (level == null || !levelNames.Contains(level, StringComparer.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                return true;
            }).ToList();
        }

        public static string GetTypeName(Document doc, Element e)
        {
            return doc.GetElement(e.GetTypeId())?.Name;
        }

        public static string GetFamilyName(Document doc, Element e)
        {
            return (doc.GetElement(e.GetTypeId()) as ElementType)?.FamilyName;
        }

        public static string GetLevelName(Document doc, Element e)
        {
            var levelId = e.LevelId;
            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                var p = e.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)
                        ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                        ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                levelId = p?.AsElementId();
            }
            return levelId == null || levelId == ElementId.InvalidElementId ? null : doc.GetElement(levelId)?.Name;
        }

        public static string GetGroupName(Document doc, Element e)
        {
            return e.GroupId == ElementId.InvalidElementId ? null : doc.GetElement(e.GroupId)?.Name;
        }

        public static object WorksharingInfo(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared)
            {
                return null;
            }
            var checkout = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
            var updates = WorksharingUtils.GetModelUpdatesStatus(doc, id);
            return new { checkoutStatus = checkout.ToString(), owner, modelUpdates = updates.ToString() };
        }

        /// <summary>
        /// Returns why an element cannot be edited right now in a workshared model, or null if it can.
        /// </summary>
        public static string WorksharingBlocker(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared)
            {
                return null;
            }
            var checkout = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
            if (checkout == CheckoutStatus.OwnedByOtherUser)
            {
                return $"owned by {owner}";
            }
            var updates = WorksharingUtils.GetModelUpdatesStatus(doc, id);
            if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
            {
                return "not latest (Reload Latest needed)";
            }
            return null;
        }

        public static object RawValue(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.AsString();
                case StorageType.Integer:
                    return p.AsInteger();
                case StorageType.Double:
                    return p.AsDouble();
                case StorageType.ElementId:
                    return p.AsElementId()?.GetValue();
                default:
                    return null;
            }
        }

        public static string DisplayValue(Parameter p)
        {
            return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
        }

        public static object DescribeParameter(Parameter p)
        {
            return new
            {
                name = p.Definition?.Name,
                value = DisplayValue(p),
                rawValue = RawValue(p),
                storageType = p.StorageType.ToString(),
                readOnly = p.IsReadOnly,
                shared = p.IsShared,
                builtInParameter = (p.Definition as InternalDefinition)?.BuiltInParameter is BuiltInParameter bip && bip != BuiltInParameter.INVALID
                    ? bip.ToString()
                    : null
            };
        }

        public static object XyzInMillimeters(XYZ point)
        {
            if (point == null)
            {
                return null;
            }
            return new
            {
                x = Math.Round(UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters), 2),
                y = Math.Round(UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters), 2),
                z = Math.Round(UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters), 2)
            };
        }

        public static object Direction(XYZ vector)
        {
            return vector == null ? null : new { x = Math.Round(vector.X, 6), y = Math.Round(vector.Y, 6), z = Math.Round(vector.Z, 6) };
        }

        /// <summary>
        /// Runs <paramref name="body"/> in one transaction. The body returns false to roll back.
        /// Warnings are dismissed and reported; errors roll the transaction back unless
        /// <paramref name="resolveErrors"/> is set, in which case Revit's default resolution is applied.
        /// </summary>
        public static TransactionOutcome RunTransaction(Document doc, string name, bool resolveErrors, Func<bool> body)
        {
            var failures = new CollectingFailuresPreprocessor(resolveErrors);
            // Some failures (e.g. "A group has been changed outside group edit mode") are posted
            // after preprocessing, during commit; they only reach the FailuresProcessing event.
            EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs> onFailures = (sender, args) =>
            {
                var accessor = args.GetFailuresAccessor();
                if (accessor.GetDocument().Equals(doc))
                {
                    args.SetProcessingResult(failures.PreprocessFailures(accessor));
                }
            };

            doc.Application.FailuresProcessing += onFailures;
            try
            {
                using (var transaction = new Transaction(doc, name))
                {
                    var options = transaction.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(failures);
                    options.SetClearAfterRollback(true);
                    transaction.SetFailureHandlingOptions(options);

                    transaction.Start();
                    bool keep;
                    try
                    {
                        keep = body();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                        {
                            transaction.RollBack();
                        }
                        throw;
                    }

                    if (!keep)
                    {
                        transaction.RollBack();
                        return new TransactionOutcome(false, "rolled back: item errors", failures.Messages);
                    }

                    var status = transaction.Commit();
                    var committed = status == TransactionStatus.Committed;
                    return new TransactionOutcome(committed, committed ? "committed" : $"rolled back: {status}", failures.Messages);
                }
            }
            finally
            {
                doc.Application.FailuresProcessing -= onFailures;
            }
        }

        /// <summary>
        /// Solids of an element, including those inside geometry instances (family instances).
        /// </summary>
        public static List<Solid> GetSolids(Element element)
        {
            var solids = new List<Solid>();
            var geometry = element.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
            if (geometry != null)
            {
                CollectSolids(geometry, solids);
            }
            return solids;
        }

        private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
        {
            foreach (var obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    solids.Add(solid);
                }
                else if (obj is GeometryInstance instance)
                {
                    var nested = instance.GetInstanceGeometry();
                    if (nested != null)
                    {
                        CollectSolids(nested, solids);
                    }
                }
            }
        }

        /// <summary>
        /// Filter for elements that touch or overlap <paramref name="element"/>: its solids are shifted
        /// by the tolerance along ±X, ±Y and ±Z, so a face-to-face contact in any direction still
        /// produces a volume overlap with at least one shifted copy. Null if the element has no solids.
        /// </summary>
        public static ElementFilter TouchingFilter(Element element, double toleranceFeet)
        {
            var solids = GetSolids(element);
            if (solids.Count == 0)
            {
                return null;
            }
            var shifts = new[]
            {
                XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY, XYZ.BasisZ, -XYZ.BasisZ
            };
            var filters = new List<ElementFilter>();
            foreach (var solid in solids)
            {
                filters.Add(new ElementIntersectsSolidFilter(solid));
                foreach (var shift in shifts)
                {
                    var moved = SolidUtils.CreateTransformed(solid, Transform.CreateTranslation(shift * toleranceFeet));
                    filters.Add(new ElementIntersectsSolidFilter(moved));
                }
            }
            return filters.Count == 1 ? filters[0] : new LogicalOrFilter(filters);
        }

        /// <summary>
        /// Candidate pairs between two element sets: bounding boxes within tolerance and, when
        /// <paramref name="onlyTouching"/>, an actual touch/overlap of geometry.
        /// </summary>
        public static List<(Element first, Element second)> FindPairs(
            Document doc, List<Element> firstSet, List<Element> secondSet, double toleranceFeet, bool onlyTouching, int maxPairs, out bool truncated)
        {
            truncated = false;
            var pairs = new List<(Element, Element)>();
            var seen = new HashSet<(long, long)>();
            var secondIds = secondSet.Select(e => e.Id).ToList();
            if (secondIds.Count == 0)
            {
                return pairs;
            }

            foreach (var first in firstSet)
            {
                var box = first.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }
                var offset = new XYZ(toleranceFeet, toleranceFeet, toleranceFeet);
                var collector = new FilteredElementCollector(doc, secondIds)
                    .WherePasses(new BoundingBoxIntersectsFilter(new Outline(box.Min - offset, box.Max + offset)));
                if (onlyTouching)
                {
                    var touching = TouchingFilter(first, toleranceFeet);
                    if (touching == null)
                    {
                        continue;
                    }
                    collector = collector.WherePasses(touching);
                }

                foreach (var second in collector)
                {
                    if (second.Id == first.Id)
                    {
                        continue;
                    }
                    var a = first.Id.GetValue();
                    var b = second.Id.GetValue();
                    if (!seen.Add(a < b ? (a, b) : (b, a)))
                    {
                        continue;
                    }
                    if (pairs.Count >= maxPairs)
                    {
                        truncated = true;
                        return pairs;
                    }
                    pairs.Add((first, second));
                }
            }
            return pairs;
        }

        public static List<(Element first, Element second)> ParsePairs(Document doc, JToken token)
        {
            var pairs = new List<(Element, Element)>();
            if (token == null || token.Type == JTokenType.Null)
            {
                return pairs;
            }
            foreach (var pair in token)
            {
                var ids = ParseIds(pair);
                if (ids.Count != 2)
                {
                    throw new ArgumentException($"Each pair needs exactly two element ids: {pair}");
                }
                var a = doc.GetElement(ids[0]) ?? throw new ArgumentException($"Element {ids[0].GetValue()} not found");
                var b = doc.GetElement(ids[1]) ?? throw new ArgumentException($"Element {ids[1].GetValue()} not found");
                pairs.Add((a, b));
            }
            return pairs;
        }

        public static object Brief(Document doc, Element e)
        {
            return new
            {
                id = e.Id.GetValue(),
                type = GetTypeName(doc, e),
                level = GetLevelName(doc, e),
                group = GetGroupName(doc, e)
            };
        }
    }

    public class TransactionOutcome
    {
        public bool Committed { get; }
        public string Status { get; }
        public List<object> Failures { get; }

        public TransactionOutcome(bool committed, string status, List<object> failures)
        {
            Committed = committed;
            Status = status;
            Failures = failures;
        }
    }

    /// <summary>
    /// Records every failure message. Warnings are dismissed; errors are resolved with Revit's
    /// default resolution only when allowed, otherwise the transaction is rolled back.
    /// </summary>
    public class CollectingFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly bool _resolveErrors;

        public List<object> Messages { get; } = new List<object>();

        public CollectingFailuresPreprocessor(bool resolveErrors)
        {
            _resolveErrors = resolveErrors;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var resolved = false;
            var unresolved = false;
            foreach (var message in failuresAccessor.GetFailureMessages())
            {
                var severity = message.GetSeverity();
                string action;
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(message);
                    action = "warning dismissed";
                }
                else if (_resolveErrors && message.HasResolutions())
                {
                    action = $"resolved: {message.GetDefaultResolutionCaption()}";
                    failuresAccessor.ResolveFailure(message);
                    resolved = true;
                }
                else
                {
                    action = "unresolved error";
                    unresolved = true;
                }

                Messages.Add(new
                {
                    severity = severity.ToString(),
                    description = message.GetDescriptionText(),
                    failingElementIds = message.GetFailingElementIds().Select(id => id.GetValue()).ToList(),
                    action
                });
            }

            if (unresolved)
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }
            return resolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }
}
