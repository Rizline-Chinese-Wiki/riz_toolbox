using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Settings;

/// <summary>The native probe and its tests share bounded unique selection and root lifetime handling.</summary>
internal static class SettingsUiProbe
{
    internal static Result<IReadOnlyDictionary<string, GameSettingRange>> Capture<TCandidate, TRoot>(
        IEnumerable<TCandidate> candidates, Func<TCandidate, TRoot?> rootCandidate,
        Func<TRoot, bool> isActiveLoaded, Func<TRoot, IReadOnlyDictionary<string, GameSettingRange>> readRanges,
        int candidateLimit = 32) where TRoot : class, IDisposable
    {
        TRoot? selected = null;
        try
        {
            int scanned = 0;
            foreach (var candidate in candidates)
            {
                if (++scanned > candidateLimit) return Failure(ErrorCode.QuotaExceeded, "Settings UI scan exceeds its candidate limit; refusing an incomplete ambiguous scan.");
                var root = rootCandidate(candidate);
                try
                {
                    if (root is null || !isActiveLoaded(root)) continue;
                    if (selected is not null) return Failure(ErrorCode.AmbiguousTarget, "Multiple active loaded settings pages match; refusing to select the first.");
                    selected = root; root = null;
                }
                finally { root?.Dispose(); }
            }
            return selected is null ? Failure(ErrorCode.NotReady, "The actual game settings page is not open/ready. Verified static settings remain readable.") :
                Result<IReadOnlyDictionary<string, GameSettingRange>>.Ok(readRanges(selected));
        }
        finally { selected?.Dispose(); }
    }

    private static Result<IReadOnlyDictionary<string, GameSettingRange>> Failure(ErrorCode code, string message)
        => Result<IReadOnlyDictionary<string, GameSettingRange>>.Fail(new(code, message));
}
