using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Services;

internal sealed class SharedContractPolicy
{
    private readonly HashSet<Assembly> _approved = new() { typeof(IPluginServices).Assembly };

    public OperationResult Allow<T>(string sha256) where T : class
    {
        var assembly = typeof(T).Assembly;
        if (assembly == typeof(IPluginServices).Assembly) return OperationResult.Ok();
        if (AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default)
            return OperationResult.Fail(ErrorCode.ContractIdentityMismatch, "Extra contract assemblies must be explicitly host-shared in the default load context.", InterPluginCapabilityIds.Services, typeof(T).FullName);
        if (assembly.GetName().Version is null || string.IsNullOrEmpty(assembly.Location))
            return OperationResult.Fail(ErrorCode.ContractIdentityMismatch, "A versioned contract assembly with a file location is required.", InterPluginCapabilityIds.Services, typeof(T).FullName);
        if (sha256?.Length != 64 || !sha256.All(Uri.IsHexDigit)) return OperationResult.Fail(ErrorCode.ContractIdentityMismatch, "A SHA-256 contract pin is required.", InterPluginCapabilityIds.Services, typeof(T).FullName);
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) return OperationResult.Fail(ErrorCode.ContractIdentityMismatch, "Contract assembly hash does not match its host pin.", InterPluginCapabilityIds.Services, typeof(T).FullName);
        var alreadyApproved = _approved.Contains(assembly);
        _approved.Add(assembly);
        var error = Contract(typeof(T));
        if (error is null) return OperationResult.Ok();
        if (!alreadyApproved) _approved.Remove(assembly);
        return OperationResult.Fail(error);
    }

    public OperationError? Contract(Type contract)
    {
        if (!contract.IsInterface || !contract.IsVisible || !Approved(contract))
            return new(ErrorCode.ContractIdentityMismatch, "Service contract must be a public interface from the exact SDK or an explicitly approved host-shared assembly.", InterPluginCapabilityIds.Services);
        var interfaces = contract.GetInterfaces().Append(contract).ToArray();
        if (interfaces.Any(type => !Approved(type) || !type.IsVisible))
            return new(ErrorCode.ContractIdentityMismatch, "Inherited interfaces must also have approved shared identities.", InterPluginCapabilityIds.Services);
        if (interfaces.Any(type => type.GetEvents().Length != 0)) return new(ErrorCode.UnsupportedContract, "Events are not allowed; use the broker's read-only message channels.", InterPluginCapabilityIds.Services);
        foreach (var method in interfaces.SelectMany(type => type.GetMethods()).Distinct())
        {
            if (method.IsStatic || method.IsGenericMethod || method.GetParameters().Length > 8)
                return new(ErrorCode.UnsupportedContract, "Static/generic methods or more than eight arguments are unsupported.", InterPluginCapabilityIds.Services);
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType.IsByRef || parameter.IsOut) return new(ErrorCode.UnsupportedContract, "Ref/out parameters are unsupported.", InterPluginCapabilityIds.Services);
                var error = Data(parameter.ParameterType);
                if (error is not null) return error;
            }
            var result = method.ReturnType;
            if (result == typeof(void) || result == typeof(Task)) continue;
            if (result.IsGenericType && result.GetGenericTypeDefinition() == typeof(Task<>)) result = result.GetGenericArguments()[0];
            var returnError = Data(result);
            if (returnError is not null) return returnError;
        }
        return null;
    }

    public OperationError? Data(Type type) => Data(type, new HashSet<Type>(), 0);
    private OperationError? Data(Type type, HashSet<Type> visited, int depth)
    {
        if (depth > 24) return new(ErrorCode.UnsupportedContract, "DTO type graph is too deep.", InterPluginCapabilityIds.Services);
        if (type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsPointer || type.IsByRef || type == typeof(object)
            || typeof(Delegate).IsAssignableFrom(type) || typeof(IDisposable).IsAssignableFrom(type) || type == typeof(Type))
            return new(ErrorCode.UnsupportedContract, "Opaque objects, pointers, delegates, handles and mutable service identities cannot cross this boundary.", InterPluginCapabilityIds.Services);
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(Version)) return null;
        if (type.IsEnum) return type.Assembly == typeof(int).Assembly || Approved(type) ? null : new(ErrorCode.ContractIdentityMismatch, "Enum assembly is not approved.", InterPluginCapabilityIds.Services);
        if (type.IsArray) return type.GetArrayRank() == 1 ? Data(type.GetElementType()!, visited, depth + 1) : new(ErrorCode.UnsupportedContract, "Multidimensional arrays are unsupported.", InterPluginCapabilityIds.Services);
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Nullable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(List<>)
                || definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(Dictionary<,>))
            {
                if ((definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(Dictionary<,>)) && type.GetGenericArguments()[0] != typeof(string))
                    return new(ErrorCode.UnsupportedContract, "DTO dictionaries require string keys.", InterPluginCapabilityIds.Services);
                foreach (var argument in type.GetGenericArguments())
                { var error = Data(argument, visited, depth + 1); if (error is not null) return error; }
                return null;
            }
        }
        if (!Approved(type) || !type.IsVisible || type.IsInterface)
            return new(ErrorCode.ContractIdentityMismatch, "DTO type '" + type.FullName + "' is not part of the shared contract identity.", InterPluginCapabilityIds.Services);
        if (!visited.Add(type)) return null;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0) return new(ErrorCode.UnsupportedContract, "Indexed DTO properties are unsupported.", InterPluginCapabilityIds.Services);
            var error = Data(property.PropertyType, visited, depth + 1); if (error is not null) return error;
        }
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        { var error = Data(field.FieldType, visited, depth + 1); if (error is not null) return error; }
        return null;
    }
    private bool Approved(Type type) => _approved.Contains(type.Assembly);
}
