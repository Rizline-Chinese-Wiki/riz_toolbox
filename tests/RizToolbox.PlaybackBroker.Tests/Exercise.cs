using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class BrokerChecks
{
    private static void Exercise(string mode, string[] args, IntPtr image, IntPtr target, IntPtr method,
        NativePlaybackGetTimeHookLease lease, GetTime invoke, Set set, Read read, NativePlaybackBinding binding,
        NativePlaybackSingleReturnAuthority policy)
    {
        if (mode == "original-seh-inflight")
        {
            set(1, 0x3f800000u, 2);
            var caught = Export<InvokeCaught>(image, "RizOwnedPlayback_Invoke");
            var release = Export<Release>(image, "RizOwnedPlayback_Release");
            var worker = Task.Run(() => { var result = caught(target, (IntPtr)binding.Player, method, out var bits, out var code); return (result, bits, code); });
            try
            {
                Check(Export<WaitEntered>(image, "RizOwnedPlayback_WaitEntered")() == 0, "Throwing original did not enter.");
                Check(lease.ReadActivity() is { Known: true, InFlight: 1 }, "Throwing original lacked actual in-flight responsibility.");
            }
            finally { release(); }
            Check(worker.Wait(15000) && worker.Result.result == 1 && worker.Result.code == 0xe1000452u,
                "Actual original SEH did not cross helper/thunk/source to the independent native caller.");
            Check(!lease.IsActive && lease.ReadActivity() is { Known: false, InFlight: 0 } && read(0) == 6,
                "Abnormal original did not complete once, withdraw, and drain its in-flight slot.");
            return;
        }
        if (mode is "withdraw-inflight" or "inflight-publish")
        {
            set(1, 0x3f800000u, 1);
            var worker = Task.Run(() => invoke((IntPtr)binding.Player, method));
            var wait = Export<WaitEntered>(image, "RizOwnedPlayback_WaitEntered");
            var release = Export<Release>(image, "RizOwnedPlayback_Release");
            try
            {
                Check(wait() == 0, "Owned original did not enter its controlled blocking region.");
                Check(lease.ReadActivity() is { Known: true, InFlight: 1 }, "Actual native in-flight original was not observed.");
                if (mode == "withdraw-inflight")
                {
                    lease.Dispose();
                    Check(!lease.IsActive && lease.ReadActivity() is { Known: true, InFlight: 1 }, "Withdrawal lost in-flight responsibility.");
                    Reject(() => lease.VerifyCurrent(image, target, method), "Withdrawn receipt remained current.");
                }
                else
                {
                    var failed = false;
                    try { lease.Publish(policy.Revision, policy with { Revision = policy.Revision + 1 }); }
                    catch (InvalidOperationException) { failed = true; }
                    Check(failed && lease.ReadCurrent().Authority == policy, "In-flight publication changed the actual baseline.");
                }
            }
            finally { release(); }
            Check(worker.Wait(15000) && worker.Result == (mode == "withdraw-inflight" ? 1 : 6), "In-flight return ignored final authority gate.");
            Check(lease.ReadActivity() is { Known: true, InFlight: 0 } && read(0) == 6, "Original did not complete once with zero native activity.");
            lease.Dispose(); return;
        }
        if (mode == "normal")
        {
            Check(lease.Withdraw(policy.Revision) && !lease.ReadCurrent().Active, "Conditional policy withdrawal failed.");
            Check(invoke((IntPtr)binding.Player, method) == 1, "Withdrawal did not preserve stock bits.");
            lease.Dispose(); lease.Dispose();
            Reject(() => lease.VerifyCurrent(image, target, method), "Disposed receipt remained current.");
            Check(lease.ReadActivity() is { Known: true, InFlight: 0 }, "Withdrawal lost retained activity authority."); return;
        }
        Tamper(mode, args, image, target, method, lease, invoke, read, binding);
    }
}
