using System.Text.Json;
using System.Text.Json.Serialization;

namespace RizToolbox.Sdk;

/// <summary>Serializes operation and domain results through their factories, including nested shared-service DTOs.</summary>
public sealed class OperationResultJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(OperationResult) || typeToConvert == typeof(Result)
        || typeToConvert.IsGenericType && (typeToConvert.GetGenericTypeDefinition() == typeof(OperationResult<>) || typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(OperationResult) ? new VoidConverter() : typeToConvert == typeof(Result) ? new DomainVoidConverter()
            : (JsonConverter)Activator.CreateInstance((typeToConvert.GetGenericTypeDefinition() == typeof(OperationResult<>)
                ? typeof(ValueConverter<>) : typeof(DomainValueConverter<>)).MakeGenericType(typeToConvert.GetGenericArguments()[0]), nonPublic: true)!;

    private static string Name(string property, JsonSerializerOptions options) => options.PropertyNamingPolicy?.ConvertName(property) ?? property;
    private static bool Property(JsonElement root, string property, JsonSerializerOptions options, out JsonElement value)
    {
        var name = Name(property, options);
        if (root.TryGetProperty(name, out value)) return true;
        if (options.PropertyNameCaseInsensitive)
            foreach (var item in root.EnumerateObject())
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) { value = item.Value; return true; }
        value = default; return false;
    }
    private static (bool Success, OperationError? Error) State(JsonElement root, JsonSerializerOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object || !Property(root, nameof(OperationResult.Success), options, out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("An operation result requires its Boolean Success field.");
        OperationError? error = Property(root, nameof(OperationResult.Error), options, out var encoded) && encoded.ValueKind != JsonValueKind.Null
            ? encoded.Deserialize<OperationError>(options) : null;
        var succeeded = success.GetBoolean();
        if (succeeded == (error is not null) || error is not null && string.IsNullOrWhiteSpace(error.Message))
            throw new JsonException("An operation result must succeed without an error or fail with a complete error.");
        return (succeeded, error);
    }
    private static void WriteState(Utf8JsonWriter writer, bool success, OperationError? error, JsonSerializerOptions options)
    {
        writer.WriteBoolean(Name(nameof(OperationResult.Success), options), success);
        writer.WritePropertyName(Name(nameof(OperationResult.Error), options)); JsonSerializer.Serialize(writer, error, options);
    }
    private static T? ReadValue<T>(JsonElement root, bool success, JsonSerializerOptions options)
    {
        var present = Property(root, nameof(OperationResult<T>.Value), options, out var encoded);
        if (success && !present) throw new JsonException("A successful operation result requires its Value field, including an explicit null.");
        var value = present ? encoded.Deserialize<T>(options) : default;
        if (!success && !EqualityComparer<T>.Default.Equals(value, default)) throw new JsonException("A failed operation result cannot carry a value.");
        return value;
    }

    private sealed class VoidConverter : JsonConverter<OperationResult>
    {
        public override OperationResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var state = State(document.RootElement, options);
            return state.Success ? OperationResult.Ok() : OperationResult.Fail(state.Error!);
        }
        public override void Write(Utf8JsonWriter writer, OperationResult value, JsonSerializerOptions options)
        { writer.WriteStartObject(); WriteState(writer, value.Success, value.Error, options); writer.WriteEndObject(); }
    }
    private sealed class ValueConverter<T> : JsonConverter<OperationResult<T>>
    {
        public override OperationResult<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement; var state = State(root, options);
            var value = ReadValue<T>(root, state.Success, options);
            return state.Success ? OperationResult<T>.Ok(value!) : OperationResult<T>.Fail(state.Error!);
        }
        public override void Write(Utf8JsonWriter writer, OperationResult<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); WriteState(writer, value.Success, value.Error, options);
            writer.WritePropertyName(Name(nameof(OperationResult<T>.Value), options)); JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteEndObject();
        }
    }
    private sealed class DomainVoidConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader); var state = State(document.RootElement, options);
            return state.Success ? Result.Ok() : Result.Fail(state.Error!);
        }
        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
        { writer.WriteStartObject(); WriteState(writer, value.Success, value.Error, options); writer.WriteEndObject(); }
    }
    private sealed class DomainValueConverter<T> : JsonConverter<Result<T>>
    {
        public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader); var root = document.RootElement; var state = State(root, options);
            var value = ReadValue<T>(root, state.Success, options);
            return state.Success ? Result<T>.Ok(value!) : Result<T>.Fail(state.Error!);
        }
        public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); WriteState(writer, value.Success, value.Error, options);
            writer.WritePropertyName(Name(nameof(Result<T>.Value), options)); JsonSerializer.Serialize(writer, value.Value, options);
            writer.WriteEndObject();
        }
    }
}
