# F-02 — A genuine Dormant scope does not imply a retained journal responsibility

This follow-up was a read-only review of the proposed Runtime bridge exception. No Runtime, Adapter, helper, or FakeNative source was edited by this reviewer for this finding. A separate assigned test file supplies the two actual owned-fixture counterexamples.

## Evidence

- E-06: The preserved `PlaybackSourceContext.cpp.snapshot:291-300` computes entry flags from a closed logical gate, but allocates filtered-call journal responsibility only when the owner was armed and the entry was either not Dormant or was `resourcePhysical`. That physical predicate requires ResourceEnabled and FreshBlocked, in addition to one of StartMoveNext, SetClip, and ProgressClip.
- E-07: `PlaybackSourceContext.cpp.snapshot:337-341` allocates a journal row only when `requiresReceipt` is true. `Notify` at lines 346-372 nevertheless invokes the registered observer for an actual non-suppressed scope even when its JournalIndex is invalid.
- E-08: `PlaybackSourceContext.cpp.snapshot:597-605` proves native current TLS, CallbackActive, exact owner/current thread, and a full frame memcmp. It does not prove that the current scope has any retained journal row. `ReadReceipt` at lines 607-613 can return completion only for an actual state-2 row with the exact sequence and token.
- E-09: The proposed role-only Runtime gate accepted ResourceEnabled plus a role from those three, Dormant without Armed, and a currently known/armed native owner. All those conditions also hold for a real ordinary logical handoff's same-value SetClip or ProgressClip call while FreshBlocked is false.

## Finding and path

1. Arm a real owned-fixture source and enter its actual ordinary Router.BeginHandoff. The native logical gate is closed, the physical owner remains known and Armed, and no initializer exists (Phase 0 / Blocked 0).
2. Invoke the actual same-value current AudioSource.SetClip or ProgressClip setter. Both produce genuine Dormant native current scopes; neither has resource-initialization journal qualification, so neither allocates a row.
3. The role-only bridge exception would issue a managed Before from that real TLS scope. Full current/token/memcmp verification succeeds because the scope is real. It still cannot ever produce a physical journal completion.
4. ProgressClip's immediately requested release remains pending forever. SetClip additionally enters the old rooted source's ordinary producer, which retains its own Call responsibility. That producer can never drain the nonexistent journal row, preventing the seven source roots from releasing even at known zero InFlight.

This is a reachable lifetime/completion defect. It does not require a fake frame, a fake native owner, a manual flag, a forced fault, a forged ticket, or a recovered source/Clock authority.

## Required qualification

The parent selected a private frame-only bit 256 issued by the native helper only after a real resourcePhysical/known-armed Dormant scope successfully allocates its journal. Runtime must require that qualification for the Before exception, then retain the exact issued origin for later return/physical evidence. Ordinary Dormant callbacks still have no managed observation. The owner Armed/Fresh/Clock/source checks remain unchanged. Historical completion must not be inferred from the owner's later FreshBlocked state.

## Assigned regressions

`source/tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceDormantChecks.cs` adds `resource-dormant-setclip` and `resource-dormant-progressclip`. Each uses the actual 20-member fixture and actual Router.BeginHandoff, requires original store/counter/MethodInfo effects and known native physical exit, requires zero added managed leaf observations and no initializer, and requires normal Dispose to release all roots and existing observations. Functional verification is pending a fresh build with the parent's r6 helper and matching Runtime bridge qualification; no test pass is claimed here.
