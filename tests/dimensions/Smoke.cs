using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Annotation;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services.AnnotationComponents;

namespace RevitMCPCommandSet.Utils
{
    public static class ModelEditingUtils { public static ElementId ToElementId(long id) => new(id); }
    public static class IdExtensions { public static long GetValue(this ElementId id) => id.Value; }
}

public static class DimensionSmoke
{
    public static object Run(Application app)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); passed.Add(name); }
        using var doc = app.NewProjectDocument(UnitSystem.Metric);
        try
        {
            Level level; ViewPlan view; Wall first, second; DetailCurve guide, reverse, missing;
            using (var tx = new Transaction(doc, "Fixture"))
            {
                tx.Start();
                level = Level.Create(doc, 0);
                var family = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.FloorPlan);
                view = ViewPlan.Create(doc, family.Id, level.Id);
                var type = (WallType)new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic).Duplicate("MCP smoke wall");
                using var structure = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> {
                    new(.1, MaterialFunctionAssignment.Finish1, ElementId.InvalidElementId),
                    new(.5, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId),
                    new(.1, MaterialFunctionAssignment.Finish2, ElementId.InvalidElementId) });
                structure.SetNumberOfShellLayers(ShellLayerType.Exterior, 1);
                structure.SetNumberOfShellLayers(ShellLayerType.Interior, 1);
                type.SetCompoundStructure(structure);
                first = Wall.Create(doc, Line.CreateBound(new XYZ(0,-10,0),new XYZ(0,10,0)),type.Id,level.Id,10,0,false,false);
                second = Wall.Create(doc, Line.CreateBound(new XYZ(10,-10,0),new XYZ(10,10,0)),type.Id,level.Id,10,0,false,false);
                guide = doc.Create.NewDetailCurve(view,Line.CreateBound(new XYZ(-2,0,0),new XYZ(12,0,0)));
                reverse = doc.Create.NewDetailCurve(view,Line.CreateBound(new XYZ(12,2,0),new XYZ(-2,2,0)));
                missing = doc.Create.NewDetailCurve(view,Line.CreateBound(new XYZ(-2,30,0),new XYZ(12,30,0)));
                tx.Commit();
            }
            var style = DimensionService.Style(doc,-1);
            var engine = new DimensionEngine(doc,view);
            JObject AsJson(object o) => JObject.FromObject(o);
            Dimension LastDimension(JObject report) => (Dimension)doc.GetElement(new ElementId(long.Parse(report["results"]![0]!["dimensionId"]!.ToString())));
            var faces = AsJson(engine.Run(new[]{guide.Id},style,false,0));
            Check(faces.Value<int>("created")==1, "finish faces create a chain");
            var dimension = LastDimension(faces);
            Check(dimension.NumberOfSegments==3, "two walls provide four references");
            Check(Math.Abs(dimension.Segments.get_Item(0).Value!.Value-.7)<1e-6,"finish thickness is 0.7 ft");
            var core = AsJson(new DimensionEngine(doc,view).Run(new[]{guide.Id},style,true,0));
            Check(core.Value<int>("created")==1,"core references create a chain");
            Check(Math.Abs(LastDimension(core).Segments.get_Item(0).Value!.Value-.5)<1e-6,"core thickness is 0.5 ft");
            var filtered = AsJson(new DimensionEngine(doc,view).Run(new[]{guide.Id,reverse.Id},style,false,1));
            Check(filtered.Value<int>("created")==2,"forward and reversed guides create dimensions");
            var lengths=filtered["results"]!.Select(r => ((Dimension)doc.GetElement(new ElementId(long.Parse(r["dimensionId"]!.ToString())))).Value!.Value).ToArray();
            Check(lengths.All(v=>Math.Abs(v-10)<1e-6),"minimum filtering is direction independent");
            var partial=AsJson(new DimensionEngine(doc,view).Run(new[]{guide.Id,missing.Id},style,false,0));
            Check(partial.Value<int>("created")==1 && partial.Value<int>("skipped")==1 && !partial.Value<bool>("success"),"partial guide failure preserves successful IDs");
            DimensionCreationInfo Item() => new() { ViewId=view.Id.Value, ElementIds=new(){first.Id.Value,second.Id.Value}, StartPoint=new(-1000,0,0),EndPoint=new(4000,0,0),LinePoint=new(0,1524,0) };
            var explicitReport=AsJson(DimensionService.Create(doc,new(){Item()}));
            Check(explicitReport.Value<int>("created")==1,"explicit wall dimension creates");
            Check(Math.Abs(((Line)LastDimension(explicitReport).Curve).Origin.Y-5)<1e-6,"linePoint offsets dimension to requested position");
            var bad=Item(); bad.ElementIds.Clear();
            var mixed=AsJson(DimensionService.Create(doc,new(){Item(),bad}));
            Check(mixed.Value<int>("created")==1 && mixed.Value<int>("failed")==1,"explicit batch reports partial success");
            Check(AsJson(DimensionService.Create(doc,new(){bad})).Value<int>("created")==0,"point-only requests fail without changes");
            var duplicate=Item(); duplicate.ElementIds=new(){first.Id.Value,first.Id.Value};
            Check(AsJson(DimensionService.Create(doc,new(){duplicate})).Value<int>("created")==0,"duplicate references fail");
            Check(SpacingFilter.Apply(new[]{0d,.005,.2},x=>x,.01).SequenceEqual(new[]{0d,.2}),"spacing filters against last retained station");
            return new { success=true, passed=passed.Count, checks=passed };
        }
        finally { doc.Close(false); }
    }
}
