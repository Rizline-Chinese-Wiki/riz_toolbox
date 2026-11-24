using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter;

internal sealed class GameDataService:ICatalogService,IScoreService
{
    private const string LoadingMessage = "Game catalog/score loading has not completed.";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly IntPtr _managerType;
    internal GameDataService(NativeRuntime api,IMainThreadDispatcher main,IntPtr managerType){_api=api;_main=main;_managerType=managerType;}
    private IntPtr Manager()=>_api.ReadStatic<IntPtr>(_managerType,"Instance");
    private DataSnapshotInput Capture(IntPtr levels, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var charts=new List<ChartInfo>();var scores=new List<ScoreRecord>();
        foreach(var level in Items(levels,cancellationToken)) {
            var songId=_api.ReadString(level,"id");
            var music=_api.Read<IntPtr>(level,"musicInfo");
            var title=music==IntPtr.Zero?songId:_api.ReadString(music,"musicName");
            var differences=_api.Read<IntPtr>(level,"chartInfos");
            foreach(var difference in Items(differences,cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();
                var chart=_api.Read<IntPtr>(difference,"chartInfo");
                var id=_api.ReadString(chart,"id");
                charts.Add(new(id,songId,title,_api.ReadString(chart,"level"),_api.Read<float>(chart,"difficulty")));
                var best=_api.Read<IntPtr>(difference,"bestScore");
                if(best!=IntPtr.Zero)scores.Add(new(id,_api.Read<int>(best,"score"),_api.Read<float>(best,"completeRate"),
                    _api.Read<int>(best,"hit"),_api.Read<int>(best,"bad"),_api.Read<int>(best,"miss"),
                    _api.Read<bool>(best,"isFullCombo"),_api.Read<bool>(best,"isClear")));
                if(charts.Count+scores.Count>20000)throw new DataCaptureQuotaException("Raw game capture exceeds 20000 combined chart/score entries.");
            }
        }
        return new(Array.AsReadOnly(charts.ToArray()),Array.AsReadOnly(scores.ToArray()));
    }
    private IEnumerable<IntPtr> Items(IntPtr list,CancellationToken cancellationToken) {
        if(list==IntPtr.Zero)yield break;
        var count=_api.Unbox<int>(_api.Call(list,"get_Count"));
        if(count<0||count>50000)throw new InvalidOperationException("Unexpected collection size.");
        for(var i=0;i<count;i++){cancellationToken.ThrowIfCancellationRequested();yield return _api.Call(list,"get_Item",A.Value(i));}
    }
    /// <summary>Both collections come from one synchronous traversal; no second native capture is needed by the revision cache.</summary>
    internal Result<DataSnapshotInput> CaptureSnapshot(CancellationToken cancellationToken=default)
    {
        if(!_main.IsMainThread)return Failure(ErrorCode.WrongThread,"Game data capture requires the Unity main thread.");
        if(cancellationToken.IsCancellationRequested)return Failure(ErrorCode.Cancelled,"Game data capture was cancelled.");
        try
        {
            _main.VerifyAccess();
            var manager = Manager();
            if (manager == IntPtr.Zero) return Failure(ErrorCode.NotReady, LoadingMessage, true);
            using var managerRoot = _api.Root(manager);
            var assets = _api.Read<IntPtr>(managerRoot.Target, "assetManager");
            if (assets == IntPtr.Zero) return Failure(ErrorCode.NotReady, LoadingMessage, true);
            using var assetRoot = _api.Root(assets);
            if (!_api.Read<bool>(assetRoot.Target, "loadAssetListOver"))
                return Failure(ErrorCode.NotReady, LoadingMessage, true);
            var control = _api.Read<IntPtr>(managerRoot.Target, "levelControl");
            if (control == IntPtr.Zero) return Failure(ErrorCode.NotReady, LoadingMessage, true);
            using var controlRoot = _api.Root(control);
            cancellationToken.ThrowIfCancellationRequested();
            var levels = _api.Call(controlRoot.Target, "GetAllLevel");
            cancellationToken.ThrowIfCancellationRequested();
            if (levels == IntPtr.Zero) return Failure(ErrorCode.NotReady, LoadingMessage, true);
            using var levelRoot = _api.Root(levels);
            var captured = Capture(levelRoot.Target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Native list calls may replace the source selected for this traversal.
            manager = managerRoot.Target;
            assets = assetRoot.Target;
            var sourceStillReady = _api.Read<bool>(assets, "loadAssetListOver")
                && _api.Read<IntPtr>(manager, "assetManager") == assets
                && _api.Read<IntPtr>(manager, "levelControl") == controlRoot.Target
                && Manager() == manager;
            cancellationToken.ThrowIfCancellationRequested();
            return sourceStillReady ? Result<DataSnapshotInput>.Ok(captured)
                : Failure(ErrorCode.NotReady, "Game catalog/score source changed during capture.", true);
        }
        catch(OperationCanceledException){return Failure(ErrorCode.Cancelled,"Game data capture was cancelled.");}
        catch(DataCaptureQuotaException ex){return Failure(ErrorCode.QuotaExceeded,ex.Message);}
        catch(UnsupportedSignatureException ex){return Failure(ErrorCode.UnsupportedSignature,ex.Message);}
        catch(MissingMemberException ex){return Failure(ErrorCode.UnsupportedSignature,ex.Message);}
        catch(TargetExpiredException ex){return Failure(ErrorCode.TargetExpired,ex.Message,true);}
        catch(Exception ex){return Failure(ErrorCode.ServiceUnavailable,ex.Message,true);}
    }
    private static Result<DataSnapshotInput> Failure(ErrorCode code,string message,bool retryable=false)
        =>Result<DataSnapshotInput>.Fail(new(code,message,DataCapabilityIds.Read,Retryable:retryable));
    public OperationResult<IReadOnlyList<ChartInfo>> GetCharts()
    {
        var captured=CaptureSnapshot();
        return captured.Success?OperationResult<IReadOnlyList<ChartInfo>>.Ok(captured.Value!.Charts)
            :OperationResult<IReadOnlyList<ChartInfo>>.Fail(captured.Error!);
    }
    public OperationResult<IReadOnlyList<ScoreRecord>> GetBestScoresSnapshot()
    {
        var captured=CaptureSnapshot();
        return captured.Success?OperationResult<IReadOnlyList<ScoreRecord>>.Ok(captured.Value!.BestScores)
            :OperationResult<IReadOnlyList<ScoreRecord>>.Fail(captured.Error!);
    }
    private sealed class DataCaptureQuotaException:Exception {internal DataCaptureQuotaException(string message):base(message){}}
}
