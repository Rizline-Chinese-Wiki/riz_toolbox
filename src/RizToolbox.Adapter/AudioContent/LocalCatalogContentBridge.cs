namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class LocalContentService
{
    internal bool IsCatalogResourceCurrent(ValidatedContentLease lease)
    {
        _main.VerifyAccess();
        return !_disposed && _published.TryGetValue(lease.Snapshot.Content, out var entry)
            && entry.Published && ReferenceEquals(entry.Snapshot, lease.Snapshot)
            && ReferenceEquals(entry.Payload, lease.Payload);
    }
}
