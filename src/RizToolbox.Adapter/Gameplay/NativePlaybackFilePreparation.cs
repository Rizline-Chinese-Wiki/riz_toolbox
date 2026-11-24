using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

// One background request over ordinary files. The worker captures no adapter,
// loaded module, broker, or native installation responsibility. The main
// pump takes completed results and validates their exact holders before use.
internal sealed class NativePlaybackFilePreparation : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<PreparedFiles> _worker;
    private bool _taken, _disposed;

    internal NativePlaybackFilePreparation(string stockPath, string stockSha256, string getTimePath, string sourcePath)
    {
        var token = _cancellation.Token;
        _worker = Task.Run(() => PreparedFiles.Read(stockPath, stockSha256, getTimePath, sourcePath, token));
    }

    internal bool TryTake([NotNullWhen(true)] out PreparedFiles? files)
    {
        if (_disposed || _taken)
            throw new ObjectDisposedException(nameof(NativePlaybackFilePreparation));
        files = null;
        if (!_worker.IsCompleted) return false;
        files = _worker.GetAwaiter().GetResult();
        _taken = true;
        return true;
    }

    // Ownership decisions are made by the main-thread pump. Native handles
    // and late worker faults are retired without joining the worker there.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_taken) { _cancellation.Dispose(); return; }

        // The worker checks the token between reads and registers no callbacks.
        // Cancellation therefore returns without waiting for disk I/O or a
        // continuation that might have to release a successful late result.
        _cancellation.Cancel();
        var cancellation = _cancellation;
        _ = _worker.ContinueWith(task =>
        {
            try
            {
                if (task.IsCompletedSuccessfully) task.Result.Dispose();
                else _ = task.Exception;
            }
            finally { cancellation.Dispose(); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    internal sealed class PreparedFiles : IDisposable
    {
        private readonly HeldDigest _stock, _getTime, _source;
        internal string GetTimePath => _getTime.Path;
        internal string GetTimeSha256 => _getTime.Sha256;
        internal string SourcePath => _source.Path;
        internal string SourceSha256 => _source.Sha256;

        private PreparedFiles(HeldDigest stock, HeldDigest getTime, HeldDigest source)
            => (_stock, _getTime, _source) = (stock, getTime, source);

        internal static PreparedFiles Read(string stockPath, string stockSha256, string getTimePath, string sourcePath,
            CancellationToken cancellation)
        {
            HeldDigest? stock = null, getTime = null, source = null;
            try
            {
                stock = new(stockPath, cancellation);
                if (!stock.Sha256.Equals(stockSha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("The stock playback image differs from the pinned file before installation.");
                getTime = new(getTimePath, cancellation);
                source = new(sourcePath, cancellation);
                cancellation.ThrowIfCancellationRequested();
                return new(stock, getTime, source);
            }
            catch
            {
                source?.Dispose(); getTime?.Dispose(); stock?.Dispose();
                throw;
            }
        }

        internal void VerifyStockImage(string loadedPath, string expectedSha256)
        {
            if (!Path.GetFullPath(loadedPath).Equals(_stock.Path, StringComparison.OrdinalIgnoreCase)
                || !expectedSha256.Equals(_stock.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("Prepared playback files belong to another exact stock image.");
            _stock.VerifyCurrent();
        }

        internal void VerifyHelpers()
        {
            _getTime.VerifyCurrent();
            _source.VerifyCurrent();
        }

        public void Dispose()
        {
            _source.Dispose(); _getTime.Dispose(); _stock.Dispose();
        }
    }

    private sealed class HeldDigest : IDisposable
    {
        private readonly FileStream _held;
        private readonly long _length;
        private readonly FileIdentity? _identity;
        internal string Path { get; }
        internal string Sha256 { get; }

        internal HeldDigest(string path, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Path = System.IO.Path.GetFullPath(path);
            // Retain write/delete denial until consumed or abandoned. The
            // exact reopened file identity matters if the name is redirected.
            _held = File.OpenRead(Path);
            try
            {
                _length = _held.Length;
                _identity = ReadIdentity(_held.SafeFileHandle);
                Sha256 = Hash(_held, cancellation);
            }
            catch { _held.Dispose(); throw; }
        }

        internal void VerifyCurrent()
        {
            if (_held.SafeFileHandle.IsClosed || _held.Length != _length)
                throw new UnsupportedSignatureException("The prepared playback file lost its original held identity.");
            using var actual = File.OpenRead(Path);
            if (actual.Length != _length)
                throw new UnsupportedSignatureException("The prepared playback path now opens a different file.");
            if (_identity is { } expected)
            {
                if (ReadIdentity(_held.SafeFileHandle) != expected || ReadIdentity(actual.SafeFileHandle) != expected)
                    throw new UnsupportedSignatureException("The prepared playback path now opens a different file.");
            }
            else if (!Hash(actual, CancellationToken.None).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The prepared playback file differs from its original full digest.");
        }

        private static string Hash(FileStream input, CancellationToken cancellation)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            try
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    hash.AppendData(buffer, 0, count);
                }
                return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public void Dispose() => _held.Dispose();
    }

    // FILE_ID_INFO matches both the volume and all 128 file identifier bits.
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileIdentity(ulong Volume, Guid Identifier);

    private static FileIdentity? ReadIdentity(SafeFileHandle handle)
        => GetFileInformationByHandleEx(handle, 18, out var identity, (uint)Marshal.SizeOf<FileIdentity>())
            && identity.Identifier != Guid.Empty ? identity : null;

    [DllImport("kernel32.dll")]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileIdentity information, uint size);
}
