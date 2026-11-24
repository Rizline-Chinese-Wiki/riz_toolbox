using System.Text.Json.Nodes;
using RizToolbox.Sdk;
using static Assert;

internal static class WorkshopChartPlayabilityChecks
{
    internal static readonly (string Name, Action Test)[] Cases =
    {
        ("Workshop binding refuses a chart that would drop a declared tap", RefuseDroppedTap),
        ("Workshop rechecks geometry when a bound content ID is republished", RecheckRepublishedChart)
    };

    private static void WriteLine(ChartFixture fixture, bool hasNote)
    {
        var root = JsonNode.Parse(ChartJson())!;
        var line = root["lines"]![0]!;
        var points = line["linePoints"]!.AsArray();
        while (points.Count > 1) points.RemoveAt(points.Count - 1);
        if (!hasNote) line["notes"]!.AsArray().Clear();
        File.WriteAllText(Path.Combine(fixture.Directory, "chart.json"), root.ToJsonString());
    }

    private static void RefuseDroppedTap()
    {
        using var fixture = new ChartFixture();
        using var context = fixture.Context();
        var view = fixture.Service.ForPlugin(context);
        WriteLine(fixture, hasNote: true);
        using var chart = fixture.Register(context, "my-chart");
        var result = view.BindWorkshopChart(111, chart.Content.Content, allowExperimental: true);
        Check(!result.Success);
        Equal(ErrorCode.ContentInvalid, result.Error!.Code);
        Equal(AudioContentCapabilityIds.WorkshopChartReplacement, result.Error.CapabilityId);
        Check(fixture.Service.AdoptResult(new(10)) is null);
        Equal(0, fixture.Instances.Count);
    }

    private static void RecheckRepublishedChart()
    {
        using var fixture = new ChartFixture();
        using var context = fixture.Context();
        WriteLine(fixture, hasNote: false);
        using var original = fixture.Register(context, "my-chart");
        using var binding = Need(fixture.Service.ForPlugin(context)
            .BindWorkshopChart(111, original.Content.Content, allowExperimental: true));
        original.Dispose();

        WriteLine(fixture, hasNote: true);
        using var broken = fixture.Register(context, "my-chart");
        Check(fixture.Service.AdoptResult(new(10)) is null);
        Equal(0, fixture.Instances.Count);
        broken.Dispose();

        WriteLine(fixture, hasNote: false);
        using var repaired = fixture.Register(context, "my-chart");
        using var adopted = NeedRoot(fixture.Service.AdoptResult(new(10)));
        Check(binding.IsActive);
        Equal(1, fixture.Instances.Count);
    }
}
