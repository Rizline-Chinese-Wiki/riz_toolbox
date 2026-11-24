namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum HoldAliveSignatureFault
    { Return, Parameter, Static, Duplicate, ParameterByRef, ReturnByRef, ReturnWidth, ReturnCode, ReturnEnum, Generic, Inflated }

    // Mutate the actual official-export fixture metadata before a fresh exact
    // lookup. The production helper sees no caller-authored metadata receipt.
    internal void HoldAliveFaultSignature(HoldAliveSignatureFault fault)
    {
        var row = _gameplayMethods.Single(pair => pair.Value.Class == _holdUnityObjectClass
            && pair.Value.Name == "op_Implicit");
        var value = row.Value;
        switch (fault)
        {
            case HoldAliveSignatureFault.Return:
                _gameplayMethods[row.Key] = value with { Return = Int32 }; break;
            case HoldAliveSignatureFault.Parameter:
                _gameplayMethods[row.Key] = value with { Parameters = new[] { Object } }; break;
            case HoldAliveSignatureFault.Static:
                _gameplayMethods[row.Key] = value with { Static = false }; break;
            case HoldAliveSignatureFault.Duplicate:
                GameplayMethodAdd(_classes[value.Class], value.Name, value.Static, value.Return, value.Parameters); break;
            case HoldAliveSignatureFault.ParameterByRef:
                _types[value.Parameters[0]] = _types[value.Parameters[0]] with { ByRef = true }; break;
            case HoldAliveSignatureFault.ReturnByRef:
                _types[value.Return] = _types[value.Return] with { ByRef = true }; break;
            case HoldAliveSignatureFault.ReturnWidth:
                _types[value.Return] = _types[value.Return] with { Size = 4 }; break;
            case HoldAliveSignatureFault.ReturnCode:
                _types[value.Return] = _types[value.Return] with { Code = 8 }; break;
            case HoldAliveSignatureFault.ReturnEnum:
                _types[value.Return] = _types[value.Return] with { EnumBase = Int32 }; break;
            case HoldAliveSignatureFault.Generic: GenericMethod = true; break;
            case HoldAliveSignatureFault.Inflated: InflatedMethod = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
