using System.Text.Json;
using RizToolbox.Sdk;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var nullSuccess = RoundTrip(Result<string?>.Ok(null));
Require(nullSuccess.Success && nullSuccess.Value is null && nullSuccess.Error is null, "Null success contract");

var unknown = new OperationError((ErrorCode)9001, "Synthetic error", Retryable: true);
var result = RoundTrip(OperationResult<int>.Fail(unknown));
Require(!result.Success && result.Error == unknown && result.Error.KnownCode == ErrorCode.Unknown, "Unknown numeric error round-trip");
Require(RoundTrip(Result.Ok()).Success, "Non-generic success round-trip");
Require(RoundTrip(OperationResult<int>.Ok(42)).Value == 42, "Generic success value round-trip");

try
{
    _ = JsonSerializer.Deserialize<Result<int>>("{\"success\":true,\"error\":null}", json);
    throw new InvalidOperationException("Malformed success was accepted.");
}
catch (JsonException)
{
    Console.WriteLine("PASS: SDK contract smoke (five checks).");
}

T RoundTrip<T>(T value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, json), json)
    ?? throw new InvalidOperationException("Expected a non-null result.");

static void Require(bool condition, string check)
{
    if (!condition) throw new InvalidOperationException("Failed: " + check);
}
