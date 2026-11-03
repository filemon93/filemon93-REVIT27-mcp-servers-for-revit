namespace RevitMCPCommandSet.Services.AnnotationComponents;

public static class SpacingFilter
{
    // Deterministic greedy filtering: keep the first station, then each sufficiently distant one.
    // A short final segment loses its final reference; no text overrides or fake values.
    public static List<T> Apply<T>(IEnumerable<T> items, Func<T, double> station, double minimum, double tolerance = 1e-7)
    {
        if ((double.IsNaN(minimum) || double.IsInfinity(minimum)) || minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum));
        var result = new List<T>();
        foreach (var item in items.OrderBy(station))
        {
            if (result.Count == 0) { result.Add(item); continue; }
            double gap = station(item) - station(result[result.Count - 1]);
            if (gap > tolerance && gap + tolerance >= minimum) result.Add(item);
        }
        return result;
    }
}
