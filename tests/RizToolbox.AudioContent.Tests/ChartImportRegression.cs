using System.Text;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Sdk;
using static Assert;

internal static class ChartImportRegression
{
    internal static (string Name, Action Test)[] Cases { get; } = new (string, Action)[]
    {
        ("Local chart registration rejects non-Single note information without publication", NoteInformation),
        ("Chart import validates explicit native chart delay and preserves signed values", ChartDelay)
    };

    private static void NoteInformation()
    {
        using var fixture = new ChartFixture();
        using var context = fixture.Context();
        var provider = fixture.Content.ForPlugin(context);
        var request = new LocalContentRequest("hold", LocalContentKind.Chart, "rizline.chart.json", 0,
            new(ContentFileArea.Package, "chart.json"));
        string path = Path.Combine(fixture.Directory, "chart.json");
        string hold = ChartJson().Replace("\"type\":0", "\"type\":2", StringComparison.Ordinal);

        foreach (string payload in new[] { "[2,\"-0.125\",2.75]", "[2,true,2.75]", "[2,{},2.75]", "[2,[],2.75]", "[2,1e100,2.75]" })
        {
            File.WriteAllText(path, hold.Replace("\"otherInformations\":[]", "\"otherInformations\":" + payload, StringComparison.Ordinal));
            var rejected = provider.Register(request);
            Equal(ErrorCode.ContentInvalid, rejected.Error?.Code);
            Equal(0, fixture.Parser.Created.Count);
            Equal(0, fixture.Content.RetainedCount);
            Equal(0, Need(provider.GetPublished()).Count);
        }

        string valid = hold.Replace("\"otherInformations\":[]", "\"otherInformations\":[2,-0.125,2.75]", StringComparison.Ordinal);
        File.WriteAllText(path, valid);
        using var registration = Need(provider.Register(request));
        Check(registration.IsPublished);
        Equal(1, Need(provider.GetPublished()).Count);
        Equal(valid, fixture.Parser.Created.Single().Source.Json);
    }

    private static void ChartDelay()
    {
        string json = ChartJson();
        byte[] WithDelay(string value) => Encoding.UTF8.GetBytes(json.Insert(1, $"\"chartDelayMs\":{value},"));
        foreach (string invalid in new[] { "\"100\"", "1.5", "2147483648", "false", "{}" })
            Throws<InvalidDataException>(() => ChartJsonValidator.Validate(WithDelay(invalid)));

        byte[] negative = WithDelay("-125");
        Equal(Encoding.UTF8.GetString(negative), ChartJsonValidator.Validate(negative).Json);
        Equal(json, ChartJsonValidator.Validate(Encoding.UTF8.GetBytes(json)).Json);
    }
}
