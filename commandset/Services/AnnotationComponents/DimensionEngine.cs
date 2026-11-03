// Adapted from filemon93/JEMS-Auto-Wymiary, commit ce4137045a990dc65d717a42fec2cd40ea3fe704.
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.AnnotationComponents;

internal sealed class DimensionEngine(Document doc, View view)
{
    private const double Eps = 1e-6;
    private readonly List<string> _notes = [];
    private readonly Dictionary<(long, int), Reference> _coreReferences = [];
    private sealed class Hit(double station, Reference reference)
    { public double Station { get; } = station; public Reference Reference { get; } = reference; }

    public object Run(IEnumerable<ElementId> guides, DimensionType type, bool core, double minimum, HashSet<ElementId> wallIds = null)
    {
        int created = 0, skipped = 0;
        var results = new List<object>();
        var walls = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Wall)).Cast<Wall>().Where(w => !w.IsHidden(view) && (wallIds == null || wallIds.Contains(w.Id))).ToList();
        using var group = new TransactionGroup(doc, "JEMS AutoWym");
        group.Start();
        foreach (var id in guides.Distinct())
        {
            using var tx = new Transaction(doc, "AutoWym — prowadnica " + id.ToString());
            try
            {
                var guide = GetGuide(doc.GetElement(id));
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(new RollbackErrors()).SetClearAfterRollback(true));
                var hits = new List<Hit>();
                foreach (var wall in walls)
                {
                    try { hits.AddRange(WallHits(wall, guide, core, type)); }
                    catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
                    catch (Exception ex)
                    {
                        _notes.Add($"Ściana {wall.Id.GetValue()}: {ex.Message}");
                    }
                }
                var filtered = SpacingFilter.Apply(hits, h => h.Station, minimum);
                if (filtered.Count < 2)
                    throw new InvalidOperationException("Mniej niż dwie zgodne referencje po filtrowaniu.");
                var refs = new ReferenceArray();
                foreach (var hit in filtered) refs.Append(hit.Reference);
                var dimension = doc.Create.NewDimension(view, guide, refs, type)
                    ?? throw new InvalidOperationException("Revit nie utworzył wymiaru.");
                doc.Regenerate();
                Verify(dimension, filtered, minimum);
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit odrzucił ciąg wymiarów.");
                created++;
                results.Add(new { guideId = id.ToString(), success = true, dimensionId = dimension.Id.ToString(), segments = filtered.Count - 1 });
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                group.RollBack();
                throw;
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                skipped++;
                results.Add(new { guideId = id.ToString(), success = false, error = ex.Message });
                _notes.Add($"Prowadnica {id.ToString()}: {ex.Message}");
            }
        }
        if (created > 0) group.Assimilate(); else group.RollBack();
        return new { success = created > 0 && skipped == 0, created, skipped, results,
            warnings = _notes.Distinct().Take(100).ToArray(), experimentalCoreReferences = core };
    }

    private Line GetGuide(Element element)
    {
        var normal = view.ViewDirection.Normalize();
        var origin = view.Origin;
        // Read solids at the plan cut plane, not at the level's bottom or a detail line's arbitrary Z.
        if (view is ViewPlan plan)
        {
            using var range = plan.GetViewRange();
            if (doc.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) is Level level)
                origin = new XYZ(origin.X, origin.Y, level.ProjectElevation + range.GetOffset(PlanViewPlane.CutPlane));
        }
        XYZ Project(XYZ p) => p - normal * (p - origin).DotProduct(normal);
        XYZ a, b;
        if (element is CurveElement { GeometryCurve: Line line })
        {
            if (Math.Abs(line.Direction.DotProduct(normal)) > Eps)
                throw new InvalidOperationException("Linia nie jest równoległa do płaszczyzny widoku.");
            a = Project(line.GetEndPoint(0)); b = Project(line.GetEndPoint(1));
        }
        else if (element is ReferencePlane rp)
        {
            using var plane = rp.GetPlane();
            var direction = plane.Normal.CrossProduct(normal);
            if (direction.GetLength() < Eps)
                throw new InvalidOperationException("Płaszczyzna odniesienia jest równoległa do widoku.");
            direction = direction.Normalize();
            var projectedNormal = plane.Normal - normal * plane.Normal.DotProduct(normal);
            var p = Project(rp.BubbleEnd);
            p += projectedNormal * ((plane.Origin - p).DotProduct(plane.Normal) / projectedNormal.DotProduct(projectedNormal));
            double t1 = (rp.BubbleEnd - p).DotProduct(direction);
            double t2 = (rp.FreeEnd - p).DotProduct(direction);
            a = p + direction * Math.Min(t1, t2); b = p + direction * Math.Max(t1, t2);
        }
        else throw new InvalidOperationException("Prowadnica nie istnieje lub nie jest prostą linią.");
        if (a.DistanceTo(b) < doc.Application.ShortCurveTolerance)
            throw new InvalidOperationException("Zbyt krótka prowadnica w tym widoku.");
        // Canonical direction makes spacing independent of the order in which the guide was drawn.
        double x = (b-a).DotProduct(view.RightDirection), y = (b-a).DotProduct(view.UpDirection);
        return x < -Eps || Math.Abs(x) <= Eps && y < 0 ? Line.CreateBound(b, a) : Line.CreateBound(a, b);
    }

    private IEnumerable<Hit> WallHits(Wall wall, Line guide, bool core, DimensionType type)
    {
        if (wall.WallType.Kind != WallKind.Basic
#if REVIT2022_OR_GREATER
            || wall.CrossSection != WallCrossSection.Vertical
#endif
            || wall.Location is not LocationCurve { Curve: Line })
        {
            _notes.Add($"Ściana {wall.Id.GetValue()}: obsługiwane są proste, pionowe ściany podstawowe.");
            return [];
        }
        var direction = guide.Direction;
        var sides = new List<(int Side, Reference Reference, PlanarFace Face, double Station)>();
        var wallAxis = (Line)((LocationCurve)wall.Location).Curve;
        for (int side = 0; side < 2; side++)
        {
            foreach (var reference in HostObjectUtils.GetSideFaces(wall, side == 0 ? ShellLayerType.Exterior : ShellLayerType.Interior))
            {
                if (wall.GetGeometryObjectFromReference(reference) is not PlanarFace face) continue;
                if (Math.Abs(face.FaceNormal.DotProduct(direction)) < 1 - Eps) continue;
                // Intersect the guide with the planar support, then test the point against the
                // wall's true finite footprint. A door/window trims the bounded face at the
                // opening, so Face.Intersect(guide) misses it even though that wall side remains
                // a valid dimension reference. The support-plane reference preserves that hit.
                double denominator = face.FaceNormal.DotProduct(direction);
                if (Math.Abs(denominator) < 1 - Eps) continue;
                double station = (face.Origin - guide.GetEndPoint(0)).DotProduct(face.FaceNormal) / denominator;
                if (station < -Eps || station > guide.Length + Eps) continue;
                XYZ point = guide.GetEndPoint(0) + direction * station;
                if (OnFiniteWallFootprint(wall, wallAxis, point))
                    sides.Add((side, reference, face, station));
            }
        }
        if (sides.Count == 0) return [];
        if (!core) return sides.Select(s => new Hit(s.Station, s.Reference)).ToList();
        using var structure = wall.WallType.GetCompoundStructure();
        if (structure == null || !structure.IsVerticallyHomogeneous() || structure.GetFirstCoreLayerIndex() < 0)
            throw new InvalidOperationException("Brak jednorodnej struktury warstw z rdzeniem.");
        var result = new List<Hit>();
        foreach (var side in sides)
        {
            double shell = 0;
            if (side.Side == 0)
                for (int i = 0; i < structure.GetFirstCoreLayerIndex(); i++) shell += structure.GetLayerWidth(i);
            else
                for (int i = structure.GetLastCoreLayerIndex() + 1; i < structure.LayerCount; i++) shell += structure.GetLayerWidth(i);
            double target = side.Station - side.Face.FaceNormal.DotProduct(direction) * shell;
            if (target < -Eps || target > guide.Length + Eps) continue;
            if (shell < Eps) { result.Add(new Hit(target, side.Reference)); continue; }
            var key = (wall.Id.GetValue(), side.Side);
            if (!_coreReferences.TryGetValue(key, out var reference))
            {
                reference = FindCoreReference(wall, guide, type, side.Reference, side.Station, target, structure.LayerCount);
                _coreReferences[key] = reference;
            }
            result.Add(new Hit(target, reference));
        }
        return result;
    }

    private bool OnFiniteWallFootprint(Wall wall, Line axis, XYZ point)
    {
        // Check along the 3D location curve so the same guard also works in elevations and sections.
        XYZ axisDirection = axis.Direction;
        XYZ fromStart = point - axis.GetEndPoint(0);
        double along = fromStart.DotProduct(axisDirection);
        double endAllowance = wall.Width + Eps;
        if (along < -endAllowance || along > axis.Length + endAllowance) return false;

        // A wall's location line may be on its center, core, or finish face. Bound lateral
        // distance in the current view plane by the full wall thickness plus join tolerance.
        XYZ perpendicular = fromStart - axisDirection * along;
        XYZ viewNormal = view.ViewDirection.Normalize();
        perpendicular -= viewNormal * perpendicular.DotProduct(viewNormal);
        return perpendicular.GetLength() <= wall.Width + endAllowance;
    }

    private Reference FindCoreReference(Wall wall, Line line, DimensionType type, Reference anchor,
        double anchorStation, double target, int layerCount)
    {
        // Internal core references are not a public API contract. Discover and validate by measured
        // position; never trust a hardcoded index and never substitute a finish face for a core face.
        foreach (int index in Enumerable.Range(1, Math.Min(64, layerCount * 2 + 8)))
        {
            using var probe = new SubTransaction(doc);
            probe.Start();
            try
            {
                var candidate = Reference.ParseFromStableRepresentation(doc, $"{wall.UniqueId}:-9999:{index}");
                var refs = new ReferenceArray(); refs.Append(anchor); refs.Append(candidate);
                var dim = doc.Create.NewDimension(view, line, refs, type);
                doc.Regenerate();
                if (dim != null && dim.Value.HasValue)
                {
                    double midpoint = (dim.Origin - line.GetEndPoint(0)).DotProduct(line.Direction);
                    double actualStation = 2 * midpoint - anchorStation;
                    if (Math.Abs(actualStation - target) < Eps
                        && Math.Abs(dim.Value.Value - Math.Abs(target - anchorStation)) < Eps)
                    { probe.RollBack(); return candidate; }
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
            finally { if (probe.GetStatus() == TransactionStatus.Started) probe.RollBack(); }
        }
        throw new InvalidOperationException("Nie odnaleziono potwierdzonej referencji lica rdzenia.");
    }

    private static void Verify(Dimension dimension, List<Hit> hits, double minimum)
    {
        var values = dimension.NumberOfSegments == 0
            ? new[] { dimension.Value ?? double.NaN }
            : dimension.Segments.Cast<DimensionSegment>().Select(s => s.Value ?? double.NaN).ToArray();
        if (values.Length != hits.Count - 1) throw new InvalidOperationException("Nieprawidłowa liczba segmentów.");
        for (int i = 0; i < values.Length; i++)
            if (!(!double.IsNaN(values[i]) && !double.IsInfinity(values[i])) || values[i] + Eps < minimum
                || Math.Abs(values[i] - (hits[i+1].Station - hits[i].Station)) > Eps)
                throw new InvalidOperationException("Wartość wymiaru nie zgadza się z położeniem referencji.");
    }

    private sealed class RollbackErrors : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            // Do not suppress model warnings or silently accept Revit's automatic corrections.
            return accessor.GetFailureMessages().Count > 0
                ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
