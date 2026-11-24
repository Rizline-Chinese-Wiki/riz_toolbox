using System.Text;
using System.Text.Json;
using RizToolbox.Runtime;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record ValidatedChartJson(string Json,int Lines,int Notes,float Bpm)
{
    internal string? PlaybackLineError { get; init; }
}
internal static class ChartJsonValidator
{
    internal const int MaximumBytes=4*1024*1024,MaximumNodes=300000,MaximumNotes=32768;
    internal static ValidatedChartJson Validate(byte[] bytes,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();if(bytes.Length<2||bytes.Length>MaximumBytes)throw new InvalidDataException("Chart JSON must be 2 bytes..4 MiB.");
        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=32,AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow});
        var root=document.RootElement;var nodes=0;Walk(root,ref nodes,token);
        if(root.ValueKind!=JsonValueKind.Object||Int(root,"fileVersion")!=0)throw new InvalidDataException("Only observed Rizline chart fileVersion 0 is adapted.");
        var names=new HashSet<string>(new[]{"fileVersion","chartDelayMs","songsName","themes","challengeTimes","bPM","bpmShifts","offset","lines","canvasMoves","cameraMove"},StringComparer.Ordinal);
        if(root.EnumerateObject().Any(x=>!names.Contains(x.Name)))throw new InvalidDataException("Chart contains undeclared top-level fields.");
        if (root.TryGetProperty("chartDelayMs", out var delay) && (delay.ValueKind != JsonValueKind.Number || !delay.TryGetInt32(out _)))
            throw new InvalidDataException("Chart field chartDelayMs must be Int32.");
        var bpm=Number(root,"bPM");if(bpm<=0||bpm>1000)throw new InvalidDataException("Chart bPM must be in (0,1000].");
        var themes=Array(root,"themes",1,256);foreach(var theme in themes.EnumerateArray())foreach(var color in Array(theme,"colorsList",3,16).EnumerateArray())Color(color);
        var canvases=Array(root,"canvasMoves",1,8192);var canvasIds=new HashSet<int>();
        foreach(var canvas in canvases.EnumerateArray())
        {
            var id=Int(canvas,"index");if(id<0||id>=8192||!canvasIds.Add(id))throw new InvalidDataException("Canvas indices must be unique and within [0,8191].");
            KeyPoints(Array(canvas,"xPositionKeyPoints",1,32768));KeyPoints(Array(canvas,"speedKeyPoints",1,32768));
        }
        var lines=Array(root,"lines",1,8192);var notes=0;
        var lineIndex = 0;
        string? playbackLineError = null;
        foreach(var line in lines.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var linePoints = Array(line, "linePoints", 1, 32768);
            var segmentCount = linePoints.GetArrayLength() - 1;
            var earliestSegmentStart = float.PositiveInfinity;
            var pointIndex = 0;
            foreach (var point in linePoints.EnumerateArray())
            {
                var pointTime = Number(point, "time");
                // The final point in the list serves only as a segment endpoint.
                if (pointIndex < segmentCount)
                    earliestSegmentStart = Math.Min(earliestSegmentStart, pointTime);
                pointIndex++;
                Number(point, "xPosition");
                Number(point, "floorPosition");
                Ease(point);
                Color(point.GetProperty("color"));
                if (!canvasIds.Contains(Int(point, "canvasIndex")))
                    throw new InvalidDataException("A line references a missing canvas index.");
            }
            foreach(var key in new[]{"judgeRingColor","lineColor"})foreach(var point in Array(line,key,0,32768).EnumerateArray())
            {Color(point.GetProperty("startColor"));Color(point.GetProperty("endColor"));Number(point,"time");}
            var lineNotes = Array(line, "notes", 0, 32768);
            // The game creates notes within segments formed by adjacent line points.
            if (playbackLineError is null && lineNotes.GetArrayLength() > 0 && segmentCount == 0)
                playbackLineError = FormattableString.Invariant($"Chart lines[{lineIndex}].linePoints must contain at least two points because this line contains notes.");
            var noteIndex = 0;
            foreach (var note in lineNotes.EnumerateArray())
            {
                if (++notes > MaximumNotes) throw new InvalidDataException("Chart exceeds 32768 notes.");
                var kind = Int(note, "type");
                var noteTime = Number(note, "time");
                Number(note, "floorPosition");
                var information = Array(note, "otherInformations", 0, 3);
                var extra = information.GetArrayLength();
                if (kind is <0 or >2 || extra is not (0 or 3) || kind == 2 && extra != 3)
                    throw new InvalidDataException("Undeclared note type or information layout.");
                if (information.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number))
                    throw new InvalidDataException("Chart otherInformations entries must be finite Single numbers.");
                if (playbackLineError is null && noteTime < earliestSegmentStart)
                    playbackLineError = FormattableString.Invariant($"Chart lines[{lineIndex}].notes[{noteIndex}].time is earlier than every segment start on this line. Add or move a segment's starting point to this time or earlier.");
                noteIndex++;
            }
            lineIndex++;
        }
        foreach(var challenge in Array(root,"challengeTimes",0,8192).EnumerateArray())
        {Number(challenge,"checkPoint");var start=Number(challenge,"start");var end=Number(challenge,"end");if(start>end||Number(challenge,"transTime")<0)throw new InvalidDataException("Invalid challenge interval or transition duration.");}
        var shifts=Array(root,"bpmShifts",1,32768);KeyPoints(shifts);if(shifts.EnumerateArray().Any(x=>Number(x,"value")<=0))throw new InvalidDataException("BPM shift multipliers must be positive.");
        var camera=root.GetProperty("cameraMove");var scales=Array(camera,"scaleKeyPoints",1,32768);KeyPoints(scales);KeyPoints(Array(camera,"xPositionKeyPoints",1,32768));
        if(scales.EnumerateArray().Any(x=>Number(x,"value")<=0))throw new InvalidDataException("Camera scales must be positive.");
        token.ThrowIfCancellationRequested();
        return new(new UTF8Encoding(false,true).GetString(bytes),lines.GetArrayLength(),notes,bpm) { PlaybackLineError = playbackLineError };
    }
    private static void Walk(JsonElement value,ref int nodes,CancellationToken token)
    {
        if(++nodes>MaximumNodes)throw new InvalidDataException("Chart exceeds the decoded JSON node budget.");if((nodes&4095)==0)token.ThrowIfCancellationRequested();
        switch(value.ValueKind)
        {
            case JsonValueKind.Object:
                var names=new HashSet<string>(StringComparer.Ordinal);foreach(var field in value.EnumerateObject()){if(!names.Add(field.Name))throw new InvalidDataException("Duplicate chart JSON properties are invalid.");Walk(field.Value,ref nodes,token);}break;
            case JsonValueKind.Array:if(value.GetArrayLength()>32768)throw new InvalidDataException("Chart array exceeds 32768 items.");foreach(var child in value.EnumerateArray())Walk(child,ref nodes,token);break;
            case JsonValueKind.Number:if(!value.TryGetSingle(out var scalar)||!float.IsFinite(scalar))throw new InvalidDataException("Chart numbers must fit finite Single storage.");break;
            case JsonValueKind.String:if(value.GetString()!.Length>4096)throw new InvalidDataException("Chart string exceeds 4096 characters.");break;
            case JsonValueKind.Null:throw new InvalidDataException("Null chart fields are outside the adapted format.");
        }
    }
    private static int Int(JsonElement o,string key)=>o.GetProperty(key).TryGetInt32(out var value)?value:throw new InvalidDataException("Chart field "+key+" must be Int32.");
    private static float Number(JsonElement o,string key)=>o.GetProperty(key).TryGetSingle(out var value)&&float.IsFinite(value)?value:throw new InvalidDataException("Chart field "+key+" must be finite Single.");
    private static void Color(JsonElement color){foreach(var key in new[]{"r","g","b","a"})if(Int(color,key) is <0 or >255)throw new InvalidDataException("Chart colors require RGBA byte channels.");}
    private static void Ease(JsonElement point){if(Int(point,"easeType") is <0 or >31)throw new InvalidDataException("Undeclared ease type outside [0,31].");}
    private static void KeyPoints(JsonElement points){foreach(var point in points.EnumerateArray()){Number(point,"time");Number(point,"value");Number(point,"floorPosition");Ease(point);}}
    private static JsonElement Array(JsonElement o,string key,int min,int max)
    {var value=o.GetProperty(key);if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()<min||value.GetArrayLength()>max)throw new InvalidDataException("Chart array "+key+" has invalid bounds.");return value;}
}
internal interface IParsedLocalChart:IDisposable { ValidatedChartJson Source{get;} }
internal interface ILocalChartParser { IParsedLocalChart Parse(byte[] bytes,CancellationToken token); }
internal sealed class NativeChartParser:ILocalChartParser
{
    private readonly NativeRuntime _api;private readonly IntPtr _chart,_json;
    internal NativeChartParser(NativeRuntime api){_api=api;_chart=api.Class("Assembly-CSharp.dll","InLevel","Chart");_json=api.Class("UnityEngine.JSONSerializeModule.dll","UnityEngine","JsonUtility");}
    public IParsedLocalChart Parse(byte[] bytes,CancellationToken token)
    {
        var source=ChartJsonValidator.Validate(bytes,token);token.ThrowIfCancellationRequested();var chart=Instantiate(source);
        if(token.IsCancellationRequested){chart.Dispose();token.ThrowIfCancellationRequested();}return chart;
    }
    internal ParsedNativeChart Instantiate(ValidatedChartJson source)
    {
        var method=_api.Method(_json,"FromJson",2,true,new[]{"System.String","System.Type"});
        var pointer=_api.Invoke(method,IntPtr.Zero,_api.Str(source.Json),A.Ref(_api.Type(_chart)));var root=_api.Root(pointer);
        try
        {
            if(!_api.ExactHookReceiverMatches(root.Target,_chart)||_api.Read<int>(root.Target,"fileVersion")!=0||Math.Abs(_api.Read<float>(root.Target,"bPM")-source.Bpm)>.0001f)
                throw new InvalidDataException("Native chart parser returned an incompatible type, version or BPM.");
            var lines=_api.Read<IntPtr>(root.Target,"lines");if(lines==IntPtr.Zero||_api.Unbox<int>(_api.Call(lines,"get_Count"))!=source.Lines)throw new InvalidDataException("Native chart parser did not preserve validated line geometry.");
            return new ParsedNativeChart(_api,root,source);
        }
        catch{root.Dispose();throw;}
    }
}
internal sealed class ParsedNativeChart:IParsedLocalChart
{
    private readonly NativeRuntime _api;private NativeRuntime.RootHandle? _root;public ValidatedChartJson Source{get;}
    internal IntPtr Pointer=>_root?.Target??throw new ObjectDisposedException(nameof(ParsedNativeChart));
    internal ParsedNativeChart(NativeRuntime api,NativeRuntime.RootHandle root,ValidatedChartJson source){_api=api;_root=root;Source=source;}
    internal NativeRuntime.RootHandle Retain()=>_api.Root(Pointer);
    public void Dispose(){_root?.Dispose();_root=null;}
}
