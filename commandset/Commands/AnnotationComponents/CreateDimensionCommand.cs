using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Commands.ModelEditing;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Services.AnnotationComponents;

namespace RevitMCPCommandSet.Commands.AnnotationComponents;
public class CreateDimensionCommand(UIApplication app) : DelegateCommandBase(app, "Create dimensions")
{
    public override string CommandName => "create_dimensions";
    public override object Execute(JObject parameters, string requestId)
    {
        var items = parameters["dimensions"]?.ToObject<List<DimensionCreationInfo>>();
        if (items == null || items.Count == 0 || items.Count > 100)
            throw new ArgumentException("Provide 1-100 dimensions.");
        return RunInRevit(doc => DimensionService.Create(doc, items));
    }
}
