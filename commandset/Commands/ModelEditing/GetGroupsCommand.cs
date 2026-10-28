using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Group types with their instances: level, origin, nesting, member counts by category,
    /// optional member ids, and worksharing state (owner / not latest).
    /// </summary>
    public class GetGroupsCommand : DelegateCommandBase
    {
        public override string CommandName => "get_groups";

        public GetGroupsCommand(UIApplication uiApp)
            : base(uiApp, "Get groups")
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var nameContains = parameters?["nameContains"]?.Value<string>();
            var includeDetailGroups = ModelEditingUtils.GetBool(parameters, "includeDetailGroups", false);
            var memberCategory = parameters?["memberCategory"]?.Value<string>();
            var includeMemberIds = ModelEditingUtils.GetBool(parameters, "includeMemberIds", false);
            var onlyWithMemberCategory = ModelEditingUtils.GetBool(parameters, "onlyWithMemberCategory", false);

            return RunInRevit(doc =>
            {
                var memberCategoryId = string.IsNullOrEmpty(memberCategory)
                    ? null
                    : ModelEditingUtils.ParseCategoryId(doc, memberCategory);

                var groupTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(GroupType))
                    .Cast<GroupType>()
                    .Where(gt => includeDetailGroups || gt.Category?.Id.GetValue() == (long)BuiltInCategory.OST_IOSModelGroups)
                    .Where(gt => string.IsNullOrEmpty(nameContains) || gt.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(gt => gt.Name)
                    .ToList();

                var result = new List<object>();
                foreach (var groupType in groupTypes)
                {
                    var instances = groupType.Groups.Cast<Group>().ToList();
                    var instanceInfos = new List<object>();
                    var categoryCounts = new Dictionary<string, int>();
                    var matchingMembers = 0;

                    foreach (var group in instances)
                    {
                        var memberIds = group.GetMemberIds();
                        var members = memberIds.Select(doc.GetElement).Where(m => m != null).ToList();
                        if (instanceInfos.Count == 0)
                        {
                            // Members are identical across instances of one type; count them once.
                            foreach (var m in members)
                            {
                                var key = m.Category?.Name ?? "(none)";
                                categoryCounts[key] = categoryCounts.TryGetValue(key, out var c) ? c + 1 : 1;
                            }
                        }
                        var filtered = memberCategoryId == null
                            ? members
                            : members.Where(m => m.Category?.Id == memberCategoryId).ToList();
                        if (memberCategoryId != null && instanceInfos.Count == 0)
                        {
                            matchingMembers = filtered.Count;
                        }

                        instanceInfos.Add(new
                        {
                            id = group.Id.GetValue(),
                            level = ModelEditingUtils.GetLevelName(doc, group),
                            origin = ModelEditingUtils.XyzInMillimeters((group.Location as LocationPoint)?.Point),
                            parentGroupId = group.GroupId == ElementId.InvalidElementId ? (long?)null : group.GroupId.GetValue(),
                            memberCount = memberIds.Count,
                            worksharing = ModelEditingUtils.WorksharingInfo(doc, group.Id),
                            editBlocker = ModelEditingUtils.WorksharingBlocker(doc, group.Id),
                            memberIds = includeMemberIds ? filtered.Select(m => m.Id.GetValue()).ToList() : null
                        });
                    }

                    if (onlyWithMemberCategory && memberCategoryId != null && matchingMembers == 0)
                    {
                        continue;
                    }

                    result.Add(new
                    {
                        groupTypeId = groupType.Id.GetValue(),
                        name = groupType.Name,
                        category = groupType.Category?.Name,
                        instanceCount = instances.Count,
                        membersPerInstanceByCategory = categoryCounts.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                        matchingMembersPerInstance = memberCategoryId == null ? (int?)null : matchingMembers,
                        instances = instanceInfos
                    });
                }

                return new { groupTypeCount = result.Count, groupTypes = result };
            });
        }
    }
}
