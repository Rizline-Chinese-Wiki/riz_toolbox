namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenOperation { Play, AddTweenToWaitingList, ExecuteTween, Update, Awake }
internal sealed record NativeTweenOperationDeclaration(NativeTweenOperation Operation, string Class, string Method,
    int Rva, NativeOperationKind Kind, IReadOnlyList<string> Parameters);

/// <summary>Fixed Steam-image observations. These declarations authorize no
/// cancellation, listener completion, or future-work admission seal.</summary>
internal static class TweenOperationProfile
{
    private static readonly NativeTweenOperationDeclaration[] Rows = {
        Row(NativeTweenOperation.Play, "TweenInfo", "Play", 0x3956F0, NativeOperationKind.Void0),
        Row(NativeTweenOperation.AddTweenToWaitingList, "TweenManager", "AddTweenToWaitingList", 0x396BE0,
            NativeOperationKind.Reference1, "TweenInfo"),
        Row(NativeTweenOperation.ExecuteTween, "TweenManager", "ExecuteTween", 0x396E40,
            NativeOperationKind.Reference1, "TweenInfo"),
        Row(NativeTweenOperation.Update, "TweenManager", "Update", 0x398D60, NativeOperationKind.Void0),
        Row(NativeTweenOperation.Awake, "TweenManager", "Awake", 0x396CF0, NativeOperationKind.Void0)
    };
    internal static IReadOnlyList<NativeTweenOperationDeclaration> All { get; } = Array.AsReadOnly(Rows);
    internal static NativeTweenOperationDeclaration Get(NativeTweenOperation operation)
        => (uint)operation < Rows.Length ? Rows[(int)operation] : throw new ArgumentOutOfRangeException(nameof(operation));
    private static NativeTweenOperationDeclaration Row(NativeTweenOperation operation, string owner, string method,
        int rva, NativeOperationKind kind, params string[] parameters)
        => new(operation, owner, method, rva, kind, Array.AsReadOnly(parameters));
}
