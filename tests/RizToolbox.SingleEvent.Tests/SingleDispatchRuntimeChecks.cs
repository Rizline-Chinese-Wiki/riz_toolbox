using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class SingleDispatchRuntimeChecks
{
    private static readonly uint[] Raw32Patterns = {
        0x00000000u, // +0.0
        0x80000000u, // -0.0
        0x7f800000u, // +Inf
        0xff800000u, // -Inf
        0x7fc01234u, // +qNaN payload
        0xffc05678u, // -qNaN payload
        0x7f800001u, // +sNaN payload
        0xffa00001u, // -sNaN payload
        0x00000001u, // min subnormal
        0x3f800000u  // 1.0f
    };

    internal static readonly (string Name, Action Run)[] Cases = {
        ("ReadSingleDispatch Site 0 InvokableCall<Single> verifies closed metadata, vtable +0x198, Delegate rooting, and all 10 raw32 bit patterns", Site0TypedInvokable),
        ("ReadSingleDispatch Site 0 CachedInvokableCall<Single> preserves m_Arg1 raw32 bits independently of dynamic Invoke bits and avoids double inspection", Site0CachedInvokable),
        ("ReadSingleDispatch Site 1 InvokableCall verifies direct zero-argument ABI and ignores XMM1", Site1DirectInvokable),
        ("ReadSingleDispatch Site 2 BaseInvokableCall roots RDX array and boxed Single before class/unbox and preserves all 10 raw32 patterns including sNaN", Site2BoxedObjectArray),
        ("ReadSingleDispatch rejects unrooted objects, header mismatches, RAX mismatches, vtable/slot mismatches, non-executable entries, and ABI violations across all sites", NegativeDispatchRejections),
        ("ReadSingleEventGraph captures rooted UnityEvent<Single> graph fields first and detects concurrent mutation", SingleEventGraphChecks)
    };

    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Single dispatch runtime assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private static void Site0TypedInvokable()
    {
        foreach (var rawBits in Raw32Patterns)
        {
            using var f = new DispatchFixture();
            var invokable = f.CreateTypedInvokable(f.ActionObject);
            var obs = f.Observation(stage: NativeTweenSingleEventStage.Before, site: 0, invokable: invokable,
                actualTarget: f.TypedInvoke.MethodPointer, argR8: f.TypedInvoke.Pointer, valueBits: rawBits,
                rax: f.InvokableSingleClass.Class);
            var target = f.Api.ReadSingleDispatch(obs, f.Root(invokable), null, f.Root, f.Module);
            Check(target.Site == 0 && target.Invokable == invokable.ToInt64()
                && target.Target == f.TypedInvoke.MethodPointer.ToInt64()
                && target.Method == f.TypedInvoke.Pointer.ToInt64()
                && target.OriginalValueBits == rawBits && target.EffectiveSingleBits == rawBits
                && !target.CachedSingle && target.ArgumentArray == 0 && target.ArgumentObjects.Count == 0
                && target.Delegate == f.ActionObject.ToInt64()
                && f.Retained.ContainsKey(invokable) && f.Retained.ContainsKey(f.ActionObject)
                && f.UnrootedObjectReads.Count == 0);
            f.NoGameCalls();
        }

        // Null Delegate on InvokableCall<Single> is permitted and does not publish IntPtr.Zero
        using var nullFixture = new DispatchFixture();
        var nullDelegateInvokable = nullFixture.CreateTypedInvokable(IntPtr.Zero);
        var nullObs = nullFixture.Observation(NativeTweenSingleEventStage.Before, 0, nullDelegateInvokable,
            nullFixture.TypedInvoke.MethodPointer, nullFixture.TypedInvoke.Pointer, 0x3f800000u,
            nullFixture.InvokableSingleClass.Class);
        var nullTarget = nullFixture.Api.ReadSingleDispatch(nullObs, nullFixture.Root(nullDelegateInvokable), null, nullFixture.Root, nullFixture.Module);
        Check(nullTarget.Delegate == 0 && !nullFixture.Retained.ContainsKey(IntPtr.Zero) && nullFixture.UnrootedObjectReads.Count == 0);
        nullFixture.NoGameCalls();
    }

    private static void Site0CachedInvokable()
    {
        foreach (var argBits in Raw32Patterns)
        {
            var invokeBits = argBits ^ 0x00000001u;
            using var f = new DispatchFixture();
            var invokable = f.CreateCachedInvokable(f.ActionObject, argBits);
            var obs = f.Observation(NativeTweenSingleEventStage.Before, 0, invokable,
                f.CachedInvoke.MethodPointer, f.CachedInvoke.Pointer, invokeBits,
                f.CachedSingleClass.Class);
            var target = f.Api.ReadSingleDispatch(obs, f.Root(invokable), null, f.Root, f.Module);
            Check(target.Site == 0 && target.Invokable == invokable.ToInt64()
                && target.Target == f.CachedInvoke.MethodPointer.ToInt64()
                && target.Method == f.CachedInvoke.Pointer.ToInt64()
                && target.OriginalValueBits == invokeBits && target.EffectiveSingleBits == argBits
                && target.CachedSingle && target.Delegate == f.ActionObject.ToInt64()
                && f.Retained.ContainsKey(invokable) && f.Retained.ContainsKey(f.ActionObject)
                && f.UnrootedObjectReads.Count == 0
                // Ensure InspectRootedCachedSingleInvokable ran ONCE (2 reads of m_Arg1 + 2 reads of Delegate + 1 PublishCachedSingleDelegateOnce = 5 field reads)
                && f.FieldReads == 5);
            f.NoGameCalls();
        }
    }

    private static void Site1DirectInvokable()
    {
        using var f = new DispatchFixture();
        var invokable = f.CreatePlainInvokable(IntPtr.Zero);
        var obs = f.Observation(NativeTweenSingleEventStage.Before, 1, invokable,
            f.PlainInvoke.MethodPointer, IntPtr.Zero, 0x7f800001u, IntPtr.Zero, xmm1Low: 0xdeadbeefu);
        var target = f.Api.ReadSingleDispatch(obs, f.Root(invokable), null, f.Root, f.Module);
        Check(target.Site == 1 && target.Invokable == invokable.ToInt64()
            && target.Target == IntPtr.Add(f.Module, 0x154e5b0).ToInt64()
            && target.Method == f.PlainInvoke.Pointer.ToInt64()
            && target.OriginalValueBits == 0x7f800001u && target.EffectiveSingleBits is null
            && !target.CachedSingle && target.ArgumentArray == 0 && target.Delegate == 0
            && f.UnrootedObjectReads.Count == 0);
        f.NoGameCalls();
    }

    private static void Site2BoxedObjectArray()
    {
        foreach (var rawBits in Raw32Patterns)
        {
            using var f = new DispatchFixture();
            var invokable = f.CreateCustomSite2Invokable();
            var box = f.CreateBoxedSingle(rawBits);
            var rdx = f.CreateObjectArray(box);
            var obs = f.Observation(NativeTweenSingleEventStage.Before, 2, invokable,
                f.CustomInvoke.MethodPointer, f.CustomInvoke.Pointer, rawBits,
                f.CustomSite2Class.Class, argRdx: rdx);
            var target = f.Api.ReadSingleDispatch(obs, f.Root(invokable), f.Root(rdx), f.Root, f.Module);
            Check(target.Site == 2 && target.Invokable == invokable.ToInt64()
                && target.Target == f.CustomInvoke.MethodPointer.ToInt64()
                && target.Method == f.CustomInvoke.Pointer.ToInt64()
                && target.OriginalValueBits == rawBits && target.EffectiveSingleBits is null
                && !target.CachedSingle && target.ArgumentArray == rdx.ToInt64()
                && target.ArgumentObjects.SequenceEqual(new[] { box.ToInt64() })
                && target.Delegate == 0
                && f.Retained.ContainsKey(invokable) && f.Retained.ContainsKey(rdx) && f.Retained.ContainsKey(box)
                && f.UnrootedObjectReads.Count == 0
                && f.Base.ActiveRoots == 3 && f.Base.FreedRoots == f.Base.NewRoots - f.Retained.Count);
            f.NoGameCalls();
        }
    }

    private static void NegativeDispatchRejections()
    {
        // 1. Site 0 negative modes
        foreach (var mode in new[] {
            "header-mismatch", "rax-mismatch", "vtable-target-mismatch", "vtable-method-mismatch",
            "virtual-resolve-mismatch", "non-executable-target", "static-method", "non-virtual-method",
            "abstract-resolved", "generic-method", "wrong-slot", "slot-zero", "slot-ffff",
            "wrong-resolved-method-name", "wrong-resolved-declaring-class", "foreign-generic-class",
            "non-inflated-class", "wrong-delegate-class", "wrong-delegate-class-cached", "slot-zero-cached",
            "cached-resolves-to-parent-invoke", "not-before-stage", "delegate-root-failed" })
        {
            using var f = new DispatchFixture();
            var isCachedMode = mode is "wrong-delegate-class-cached" or "slot-zero-cached" or "cached-resolves-to-parent-invoke";
            var invokable = isCachedMode
                ? f.CreateCachedInvokable(f.ActionObject, 0x3f800000u)
                : f.CreateTypedInvokable(f.ActionObject);
            var actualTarget = isCachedMode ? f.CachedInvoke.MethodPointer : f.TypedInvoke.MethodPointer;
            var actualMethod = isCachedMode ? f.CachedInvoke.Pointer : f.TypedInvoke.Pointer;
            var cls = isCachedMode ? f.CachedSingleClass.Class : f.InvokableSingleClass.Class;
            var rax = cls;
            var stage = NativeTweenSingleEventStage.Before;
            var invokableRoot = f.Root(invokable);
            switch (mode)
            {
                case "header-mismatch": Marshal.WriteIntPtr(invokable, f.BaseInvokableClass.Class); break;
                case "rax-mismatch": rax = f.BaseInvokableClass.Class; break;
                case "vtable-target-mismatch": Marshal.WriteIntPtr(cls, 0x198, IntPtr.Add(actualTarget, 4)); break;
                case "vtable-method-mismatch": Marshal.WriteIntPtr(cls, 0x1a0, f.CachedInvoke.Pointer); break;
                case "virtual-resolve-mismatch": f.VirtualResolutionOverride = f.CachedInvoke.Pointer; break;
                case "non-executable-target":
                    actualTarget = f.AllocateRaw(16);
                    Marshal.WriteIntPtr(cls, 0x198, actualTarget);
                    f.UpdateMethodPointer(f.TypedInvoke, actualTarget);
                    break;
                case "static-method": f.UpdateMethodFlags(f.TypedInvoke, 0x50); break;
                case "non-virtual-method": f.UpdateMethodFlags(f.TypedInvoke, 0x00); break;
                case "abstract-resolved": f.UpdateMethodFlags(f.TypedInvoke, 0x440); break;
                case "generic-method": f.TypedInvoke.Generic = true; break;
                case "wrong-slot": f.UpdateMethodSlot(f.TypedInvoke, 4); break;
                case "slot-zero": f.UpdateMethodSlot(f.TypedInvoke, 0); break;
                case "slot-ffff": f.UpdateMethodSlot(f.TypedInvoke, ushort.MaxValue); break;
                case "slot-zero-cached":
                    f.UpdateMethodSlot(f.TypedInvoke, 0);
                    f.UpdateMethodSlot(f.CachedInvoke, 0);
                    break;
                case "cached-resolves-to-parent-invoke":
                    actualTarget = f.TypedInvoke.MethodPointer;
                    actualMethod = f.TypedInvoke.Pointer;
                    Marshal.WriteIntPtr(cls, 0x198, actualTarget);
                    Marshal.WriteIntPtr(cls, 0x1a0, actualMethod);
                    f.VirtualResolutionOverride = actualMethod;
                    break;
                case "wrong-resolved-method-name":
                {
                    var hijack = f.AddMethod(cls, "Hijack", f.VoidClass.Type, new[] { f.SingleClass.Type }, actualTarget, flags: 0x40, slot: 6);
                    actualMethod = hijack.Pointer;
                    Marshal.WriteIntPtr(cls, 0x1a0, actualMethod);
                    f.VirtualResolutionOverride = actualMethod;
                    break;
                }
                case "wrong-resolved-declaring-class":
                {
                    var hijack = f.AddMethod(f.CustomSite2Class.Class, "Invoke", f.VoidClass.Type, new[] { f.SingleClass.Type }, actualTarget, flags: 0x40, slot: 6);
                    actualMethod = hijack.Pointer;
                    Marshal.WriteIntPtr(cls, 0x1a0, actualMethod);
                    f.VirtualResolutionOverride = actualMethod;
                    break;
                }
                case "foreign-generic-class": f.InvokableSingleClass.Image = f.GameImage; break;
                case "non-inflated-class": f.InvokableSingleClass.Inflated = false; break;
                case "wrong-delegate-class": Marshal.WriteIntPtr(f.ActionObject, f.ObjectClass.Class); break;
                case "wrong-delegate-class-cached": Marshal.WriteIntPtr(f.ActionObject, f.Int32Class.Class); break;
                case "not-before-stage": stage = NativeTweenSingleEventStage.Returned; break;
                case "delegate-root-failed":
                    f.BeforePublishedRoot = ptr => { if (ptr == f.ActionObject) throw new IOException("delegate root rejected"); };
                    break;
            }
            var obs = f.Observation(stage, 0, invokable, actualTarget, actualMethod, 0x3f800000u, rax);
            if (mode is "delegate-root-failed")
                Throws<IOException>(() => f.Api.ReadSingleDispatch(obs, invokableRoot, null, f.Root, f.Module));
            else if (mode is "static-method")
                Throws<MissingMethodException>(() => f.Api.ReadSingleDispatch(obs, invokableRoot, null, f.Root, f.Module));
            else
                Throws<UnsupportedSignatureException>(() => f.Api.ReadSingleDispatch(obs, invokableRoot, null, f.Root, f.Module));
            Check(f.UnrootedObjectReads.Count == 0);
            f.NoGameCalls();
        }

        // 2. Site 1 negative modes
        foreach (var mode in new[] { "subclass", "wrong-rva", "nonzero-rdx", "abstract-generic-method", "virtual-method", "inflated-method" })
        {
            using var f = new DispatchFixture();
            var invokable = mode == "subclass" ? f.CreateCustomSite2Invokable() : f.CreatePlainInvokable(IntPtr.Zero);
            var target = mode == "wrong-rva" ? IntPtr.Add(f.Module, 0x154e5b4) : f.PlainInvoke.MethodPointer;
            var rdx = mode == "nonzero-rdx" ? f.CreateObjectArray(IntPtr.Zero) : IntPtr.Zero;
            switch (mode)
            {
                case "abstract-generic-method":
                    f.UpdateMethodFlags(f.PlainInvoke, 0x440);
                    f.PlainInvoke.Generic = true;
                    break;
                case "virtual-method":
                    f.UpdateMethodFlags(f.PlainInvoke, 0x40);
                    break;
                case "inflated-method":
                    f.PlainInvoke.Inflated = true;
                    break;
            }
            var obs = f.Observation(NativeTweenSingleEventStage.Before, 1, invokable, target, IntPtr.Zero, 0x3f800000u, IntPtr.Zero, argRdx: rdx);
            Throws<UnsupportedSignatureException>(() => f.Api.ReadSingleDispatch(obs, f.Root(invokable), null, f.Root, f.Module));
            Check(f.UnrootedObjectReads.Count == 0);
        }

        // 3. Site 2 negative modes (including rejecting InvokableCall / InvokableCall<Single> / CachedInvokableCall<Single>, subclasses, open generics, slot 0/0xFFFF, resolved name/declaring class, and sNaN bit mismatch)
        foreach (var mode in new[] {
            "reject-plain-invokable", "reject-typed-invokable", "reject-cached-invokable",
            "site2-subclass-of-plain", "site2-subclass-of-typed", "site2-subclass-of-cached",
            "site2-open-generic", "site2-open-generic-code18",
            "missing-rdx-root", "wrong-array-length", "null-box", "non-single-box",
            "box-header-mismatch", "snan-bit-mismatch", "wrong-slot", "slot-zero", "slot-ffff",
            "wrong-resolved-method-name", "box-root-failed" })
        {
            using var f = new DispatchFixture();
            IntPtr invokable;
            var actualTarget = f.CustomInvoke.MethodPointer;
            var actualMethod = f.CustomInvoke.Pointer;
            switch (mode)
            {
                case "reject-plain-invokable": invokable = f.CreatePlainInvokable(IntPtr.Zero); break;
                case "reject-typed-invokable": invokable = f.CreateTypedInvokable(IntPtr.Zero); break;
                case "reject-cached-invokable": invokable = f.CreateCachedInvokable(IntPtr.Zero, 0x3f800000u); break;
                case "site2-subclass-of-plain":
                case "site2-subclass-of-typed":
                case "site2-subclass-of-cached":
                case "site2-open-generic":
                case "site2-open-generic-code18":
                {
                    var parent = mode switch {
                        "site2-subclass-of-plain" => f.PlainInvokableClass.Class,
                        "site2-subclass-of-typed" => f.InvokableSingleClass.Class,
                        "site2-subclass-of-cached" => f.CachedSingleClass.Class,
                        _ => f.BaseInvokableClass.Class
                    };
                    var code = mode == "site2-open-generic" ? 21 : 18;
                    var sub = f.AddType("Fixture.Site2Sub_" + mode, "Fixture", "Site2Sub_" + mode, code, false, 8, f.GameImage, parent);
                    if (mode is "site2-open-generic" or "site2-open-generic-code18") { sub.Generic = true; sub.Inflated = false; }
                    var m = f.AddMethod(sub.Class, "Invoke", f.VoidClass.Type, new[] { f.ObjectArrayClass.Type }, f.CustomInvoke.MethodPointer, flags: 0x40, slot: 4);
                    actualTarget = m.MethodPointer;
                    actualMethod = m.Pointer;
                    invokable = f.AllocateRaw(32);
                    Marshal.WriteIntPtr(invokable, sub.Class);
                    break;
                }
                default:
                    invokable = f.CreateCustomSite2Invokable();
                    break;
            }
            var invClass = Marshal.ReadIntPtr(invokable);
            Marshal.WriteIntPtr(invClass, 0x178, actualTarget);
            Marshal.WriteIntPtr(invClass, 0x180, actualMethod);
            if (mode == "wrong-slot") f.UpdateMethodSlot(f.CustomInvoke, 6);
            if (mode == "slot-zero") f.UpdateMethodSlot(f.CustomInvoke, 0);
            if (mode == "slot-ffff") f.UpdateMethodSlot(f.CustomInvoke, ushort.MaxValue);
            if (mode == "wrong-resolved-method-name")
            {
                var hijack = f.AddMethod(invClass, "Hijack", f.VoidClass.Type, new[] { f.ObjectArrayClass.Type }, f.CustomInvoke.MethodPointer, flags: 0x40, slot: 4);
                actualMethod = hijack.Pointer;
                Marshal.WriteIntPtr(invClass, 0x180, actualMethod);
                f.VirtualResolutionOverride = actualMethod;
            }
            var boxBits = mode == "snan-bit-mismatch" ? 0x7f800002u : 0x7f800001u;
            var box = mode == "null-box" ? IntPtr.Zero
                : mode == "non-single-box" ? f.CreateBoxedInt32(unchecked((int)0x7f800001u))
                : f.CreateBoxedSingle(boxBits);
            if (mode == "box-header-mismatch") Marshal.WriteIntPtr(box, f.Int32Class.Class);
            if (mode == "box-root-failed")
                f.BeforePublishedRoot = ptr => { if (ptr == box) throw new IOException("box root rejected"); };
            var rdx = mode == "wrong-array-length" ? f.CreateObjectArray(box, box) : f.CreateObjectArray(box);
            var obs = f.Observation(NativeTweenSingleEventStage.Before, 2, invokable,
                actualTarget, actualMethod, 0x7f800001u, invClass, argRdx: rdx);
            var rdxRoot = mode == "missing-rdx-root" ? null : f.Root(rdx);
            if (mode == "box-root-failed")
                Throws<IOException>(() => f.Api.ReadSingleDispatch(obs, f.Root(invokable), rdxRoot, f.Root, f.Module));
            else
                Throws<UnsupportedSignatureException>(() => f.Api.ReadSingleDispatch(obs, f.Root(invokable), rdxRoot, f.Root, f.Module));
            Check(f.UnrootedObjectReads.Count == 0);
            f.NoGameCalls();
        }

        // 4. Site 2 positive check for a closed generic multi-arg InvokableCall<T1, T2> (not InvokableCall<Single>)
        {
            using var f = new DispatchFixture();
            var closedMulti = f.AddType("UnityEngine.Events.InvokableCall<System.Int32, System.Single>",
                "UnityEngine.Events", "InvokableCall`2", 21, false, 8, f.UnityImage, f.BaseInvokableClass.Class);
            var invoke = f.AddMethod(closedMulti.Class, "Invoke", f.VoidClass.Type, new[] { f.ObjectArrayClass.Type }, f.CustomInvoke.MethodPointer, flags: 0x40, slot: 4);
            Marshal.WriteIntPtr(closedMulti.Class, 0x178, invoke.MethodPointer);
            Marshal.WriteIntPtr(closedMulti.Class, 0x180, invoke.Pointer);
            var invokable = f.AllocateRaw(32);
            Marshal.WriteIntPtr(invokable, closedMulti.Class);
            var box = f.CreateBoxedSingle(0x80000000u);
            var rdx = f.CreateObjectArray(box);
            var obs = f.Observation(NativeTweenSingleEventStage.Before, 2, invokable,
                invoke.MethodPointer, invoke.Pointer, 0x80000000u, closedMulti.Class, argRdx: rdx);
            var target = f.Api.ReadSingleDispatch(obs, f.Root(invokable), f.Root(rdx), f.Root, f.Module);
            Check(target.Site == 2 && target.OriginalValueBits == 0x80000000u && f.UnrootedObjectReads.Count == 0);
            f.NoGameCalls();
        }
    }

    private static void SingleEventGraphChecks()
    {
        using var f = new DispatchFixture();
        var (evt, calls, group, persistList, runtimeList, execList, groupList) = f.CreateEmptySingleEventGraph();
        var eventTarget = new VerifiedHookTarget("test.single.invoke", f.TypedInvoke.MethodPointer, f.TypedInvoke.Pointer,
            f.SingleEventClass.Class, ExactHookAbi.OperationSingle1Void);
        var graph = f.Api.ReadSingleEventGraph(f.Root(evt), eventTarget, f.Root);
        Check(graph.Event == evt.ToInt64() && graph.Calls == calls.ToInt64() && graph.PersistentGroup == group.ToInt64()
            && !graph.CallsDirty && !graph.NeedsUpdate
            && graph.PersistentGroupCalls.Count == 0 && graph.PersistentCalls.Count == 0
            && graph.RuntimeCalls.Count == 0 && graph.ExecutingCalls.Count == 0
            && graph.PersistentGroupCalls.List == groupList.ToInt64()
            && graph.PersistentCalls.List == persistList.ToInt64()
            && graph.RuntimeCalls.List == runtimeList.ToInt64()
            && graph.ExecutingCalls.List == execList.ToInt64()
            && f.UnrootedObjectReads.Count == 0);
        f.NoGameCalls();

        // Mutating m_CallsDirty mid-read must fail closed
        using var mutating = new DispatchFixture();
        var (mutEvt, _, _, _, _, _, _) = mutating.CreateEmptySingleEventGraph();
        var mutTarget = new VerifiedHookTarget("test.single.invoke", mutating.TypedInvoke.MethodPointer, mutating.TypedInvoke.Pointer,
            mutating.SingleEventClass.Class, ExactHookAbi.OperationSingle1Void);
        var flipped = false;
        mutating.AfterFieldRead = field => {
            if (!flipped && field.Name == "m_NeedsUpdate")
            { flipped = true; Marshal.WriteByte(mutEvt, 0x20, 1); }
        };
        Throws<UnsupportedSignatureException>(() => mutating.Api.ReadSingleEventGraph(mutating.Root(mutEvt), mutTarget, mutating.Root));
        Check(mutating.UnrootedObjectReads.Count == 0);

        // ProbeC: Swapping m_Calls.m_RuntimeCalls mid-read (on 4th _items read while m_ExecutingCalls is read) must fail closed
        using var swapFixture = new DispatchFixture();
        var (swapEvt, swapCalls, _, _, _, _, _) = swapFixture.CreateEmptySingleEventGraph();
        var replacementRuntimeList = swapFixture.CreateList(swapFixture.BaseListClass, swapFixture.BaseCallArrayClass);
        var swapTarget = new VerifiedHookTarget("test.single.invoke", swapFixture.TypedInvoke.MethodPointer, swapFixture.TypedInvoke.Pointer,
            swapFixture.SingleEventClass.Class, ExactHookAbi.OperationSingle1Void);
        var itemsReads = 0;
        var swapped = false;
        swapFixture.AfterFieldRead = field => {
            if (field.Name == "_items" && ++itemsReads == 4 && !swapped)
            {
                swapped = true;
                Marshal.WriteIntPtr(swapCalls, 0x18, replacementRuntimeList);
            }
        };
        Throws<UnsupportedSignatureException>(() => swapFixture.Api.ReadSingleEventGraph(swapFixture.Root(swapEvt), swapTarget, swapFixture.Root));
        Check(swapped && swapFixture.UnrootedObjectReads.Count == 0);

        // ProbeE: Mutating _version during element verification in ReadSingleListCore must fail closed
        using var versionFixture = new DispatchFixture();
        var item0 = versionFixture.CreatePlainInvokable(IntPtr.Zero);
        var versionExecList = versionFixture.CreateList(versionFixture.BaseListClass, versionFixture.BaseCallArrayClass, item0);
        var versionEvt = versionFixture.AllocateRaw(48);
        Marshal.WriteIntPtr(versionEvt, versionFixture.SingleEventClass.Class);
        var versionTarget = new VerifiedHookTarget("test.single.invoke", versionFixture.TypedInvoke.MethodPointer, versionFixture.TypedInvoke.Pointer,
            versionFixture.SingleEventClass.Class, ExactHookAbi.OperationSingle1Void);
        var itemRootCalls = 0;
        var versionMutated = false;
        versionFixture.BeforePublishedRoot = ptr => {
            if (ptr == item0 && ++itemRootCalls == 2 && !versionMutated)
            {
                versionMutated = true;
                Marshal.WriteInt32(versionExecList, 0x1c, 99);
            }
        };
        Throws<UnsupportedSignatureException>(() => versionFixture.Api.ReadSingleExecutingList(
            versionFixture.Root(versionEvt), versionFixture.Root(versionExecList), versionTarget, versionFixture.Root));
        Check(versionMutated && versionFixture.UnrootedObjectReads.Count == 0);
    }

    private sealed class DispatchFixture : IDisposable
    {
        internal sealed class TypeRow
        {
            internal string Name = "", Namespace = "", ShortName = "";
            internal IntPtr Type, Class, Parent, Image, Element;
            internal int Code, Size; internal uint InstanceSize = 64, Rank = 1;
            internal bool Value, ByRef, Generic, Inflated;
        }
        internal sealed class FieldRow
        { internal string Name = ""; internal IntPtr Pointer, Class, Type; internal int Offset; internal uint Flags; }
        internal sealed class MethodRow
        {
            internal string Name = "";
            internal IntPtr Pointer, NamePtr, Class, Return, ParametersPtr, MethodPointer;
            internal IntPtr[] Parameters = Array.Empty<IntPtr>();
            internal uint Flags = 0x40, Implementation;
            internal ushort Slot;
            internal bool Generic, Inflated;
        }

        internal readonly FakeNative Base = new();
        private readonly SyntheticPeImage _image = new();
        private readonly List<IntPtr> _allocations = new();
        private readonly Dictionary<string, Delegate> _exports = new();
        private readonly Dictionary<IntPtr, TypeRow> _types = new(), _classes = new();
        private readonly List<FieldRow> _fields = new();
        private readonly List<MethodRow> _methods = new();
        private readonly Dictionary<(IntPtr, string, string), IntPtr> _definitions = new();
        private readonly IntPtr _assemblies;
        internal readonly IntPtr Module, SystemImage, UnityImage, GameImage;
        internal readonly TypeRow VoidClass, ObjectClass, Int32Class, SingleClass, DelegateClass, MulticastClass;
        internal readonly TypeRow BaseInvokableClass, PlainInvokableClass, InvokableSingleClass, CachedSingleClass, CustomSite2Class, ActionSingleClass;
        internal readonly TypeRow ObjectArrayClass, BaseCallArrayClass, PersistentCallArrayClass, BaseListClass, PersistentListClass;
        internal readonly TypeRow UnityEventBaseClass, SingleEventClass, CallsClass, GroupClass, PersistentCallClass;
        internal readonly MethodRow PlainInvoke, BaseInvokeObjArray, CustomInvoke, TypedInvoke, CachedInvoke, ActionInvoke;
        internal readonly IntPtr ActionObject;
        internal readonly NativeRuntime Api;
        internal readonly Dictionary<IntPtr, NativeRuntime.RootHandle> Retained = new();
        internal readonly List<IntPtr> UnrootedObjectReads = new();
        internal IntPtr? VirtualResolutionOverride;
        internal int FieldReads;
        internal Action<FieldRow>? AfterFieldRead;
        internal Action<IntPtr>? BeforePublishedRoot;

        internal DispatchFixture()
        {
            Module = _image.Address;
            SystemImage = AllocateRaw(16); UnityImage = AllocateRaw(16); GameImage = AllocateRaw(16);
            _assemblies = AllocateRaw(24);
            Marshal.WriteIntPtr(_assemblies, 0, SystemImage);
            Marshal.WriteIntPtr(_assemblies, 8, UnityImage);
            Marshal.WriteIntPtr(_assemblies, 16, GameImage);

            VoidClass = AddType("System.Void", "System", "Void", 1, true, 0, SystemImage);
            ObjectClass = AddType("System.Object", "System", "Object", 28, false, 8, SystemImage);
            Int32Class = AddType("System.Int32", "System", "Int32", 8, true, 4, SystemImage);
            SingleClass = AddType("System.Single", "System", "Single", 12, true, 4, SystemImage);
            DelegateClass = AddType("System.Delegate", "System", "Delegate", 18, false, 8, SystemImage, ObjectClass.Class);
            MulticastClass = AddType("System.MulticastDelegate", "System", "MulticastDelegate", 18, false, 8, SystemImage, DelegateClass.Class);
            BaseInvokableClass = AddType("UnityEngine.Events.BaseInvokableCall", "UnityEngine.Events", "BaseInvokableCall", 18, false, 8, UnityImage, ObjectClass.Class);
            PlainInvokableClass = AddType("UnityEngine.Events.InvokableCall", "UnityEngine.Events", "InvokableCall", 18, false, 8, UnityImage, BaseInvokableClass.Class);
            InvokableSingleClass = AddType("UnityEngine.Events.InvokableCall<System.Single>", "UnityEngine.Events", "InvokableCall`1", 21, false, 8, UnityImage, BaseInvokableClass.Class);
            CachedSingleClass = AddType("UnityEngine.Events.CachedInvokableCall<System.Single>", "UnityEngine.Events", "CachedInvokableCall`1", 21, false, 8, UnityImage, InvokableSingleClass.Class);
            CustomSite2Class = AddType("Fixture.CustomInvokableCall", "Fixture", "CustomInvokableCall", 18, false, 8, GameImage, BaseInvokableClass.Class);
            ActionSingleClass = AddType("UnityEngine.Events.UnityAction<System.Single>", "UnityEngine.Events", "UnityAction`1", 21, false, 8, UnityImage, MulticastClass.Class);
            ObjectArrayClass = AddType("System.Object[]", "System", "Object[]", 29, false, 8, SystemImage, ObjectClass.Class);
            ObjectArrayClass.Element = ObjectClass.Class;
            BaseCallArrayClass = AddType("UnityEngine.Events.BaseInvokableCall[]", "UnityEngine.Events", "BaseInvokableCall[]", 29, false, 8, UnityImage, ObjectClass.Class);
            BaseCallArrayClass.Element = BaseInvokableClass.Class;
            PersistentCallClass = AddType("UnityEngine.Events.PersistentCall", "UnityEngine.Events", "PersistentCall", 18, false, 8, UnityImage, ObjectClass.Class);
            PersistentCallArrayClass = AddType("UnityEngine.Events.PersistentCall[]", "UnityEngine.Events", "PersistentCall[]", 29, false, 8, UnityImage, ObjectClass.Class);
            PersistentCallArrayClass.Element = PersistentCallClass.Class;

            BaseListClass = AddType("System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>", "System.Collections.Generic", "List`1", 21, false, 8, SystemImage, ObjectClass.Class);
            PersistentListClass = AddType("System.Collections.Generic.List<UnityEngine.Events.PersistentCall>", "System.Collections.Generic", "List`1", 21, false, 8, SystemImage, ObjectClass.Class);
            // Keep ClassFromName returning BaseListClass for List`1 only if needed; fields use direct Type pointers.
            UnityEventBaseClass = AddType("UnityEngine.Events.UnityEventBase", "UnityEngine.Events", "UnityEventBase", 18, false, 8, UnityImage, ObjectClass.Class);
            SingleEventClass = AddType("UnityEngine.Events.UnityEvent<System.Single>", "UnityEngine.Events", "UnityEvent`1", 21, false, 8, UnityImage, UnityEventBaseClass.Class);
            CallsClass = AddType("UnityEngine.Events.InvokableCallList", "UnityEngine.Events", "InvokableCallList", 18, false, 8, UnityImage, ObjectClass.Class);
            GroupClass = AddType("UnityEngine.Events.PersistentCallGroup", "UnityEngine.Events", "PersistentCallGroup", 18, false, 8, UnityImage, ObjectClass.Class);
            var booleanClass = AddType("System.Boolean", "System", "Boolean", 2, true, 1, SystemImage);

            AddField(InvokableSingleClass.Class, "Delegate", ActionSingleClass.Type, 0x10);
            AddField(CachedSingleClass.Class, "m_Arg1", SingleClass.Type, 0x18);
            AddField(UnityEventBaseClass.Class, "m_Calls", CallsClass.Type, 0x10);
            AddField(UnityEventBaseClass.Class, "m_PersistentCalls", GroupClass.Type, 0x18);
            AddField(UnityEventBaseClass.Class, "m_CallsDirty", booleanClass.Type, 0x20);
            AddField(CallsClass.Class, "m_PersistentCalls", BaseListClass.Type, 0x10);
            AddField(CallsClass.Class, "m_RuntimeCalls", BaseListClass.Type, 0x18);
            AddField(CallsClass.Class, "m_ExecutingCalls", BaseListClass.Type, 0x20);
            AddField(CallsClass.Class, "m_NeedsUpdate", booleanClass.Type, 0x28);
            AddField(GroupClass.Class, "m_Calls", PersistentListClass.Type, 0x10);
            AddField(BaseListClass.Class, "_items", BaseCallArrayClass.Type, 0x10);
            AddField(BaseListClass.Class, "_size", Int32Class.Type, 0x18);
            AddField(BaseListClass.Class, "_version", Int32Class.Type, 0x1c);
            AddField(PersistentListClass.Class, "_items", PersistentCallArrayClass.Type, 0x10);
            AddField(PersistentListClass.Class, "_size", Int32Class.Type, 0x18);
            AddField(PersistentListClass.Class, "_version", Int32Class.Type, 0x1c);

            PlainInvoke = AddMethod(PlainInvokableClass.Class, "Invoke", VoidClass.Type, Array.Empty<IntPtr>(), IntPtr.Add(Module, 0x154e5b0), flags: 0x00, slot: 0);
            BaseInvokeObjArray = AddMethod(BaseInvokableClass.Class, "Invoke", VoidClass.Type, new[] { ObjectArrayClass.Type }, IntPtr.Zero, flags: 0x440, slot: 4);
            CustomInvoke = AddMethod(CustomSite2Class.Class, "Invoke", VoidClass.Type, new[] { ObjectArrayClass.Type }, IntPtr.Add(Module, 0x1551200), flags: 0x40, slot: 4);
            TypedInvoke = AddMethod(InvokableSingleClass.Class, "Invoke", VoidClass.Type, new[] { SingleClass.Type }, IntPtr.Add(Module, 0x1551000), flags: 0x40, slot: 6);
            CachedInvoke = AddMethod(CachedSingleClass.Class, "Invoke", VoidClass.Type, new[] { SingleClass.Type }, IntPtr.Add(Module, 0x1551100), flags: 0x40, slot: 6);
            ActionInvoke = AddMethod(ActionSingleClass.Class, "Invoke", VoidClass.Type, new[] { SingleClass.Type }, IntPtr.Add(Module, 0x1551300), flags: 0x40, slot: 0);

            Marshal.WriteIntPtr(InvokableSingleClass.Class, 0x198, TypedInvoke.MethodPointer);
            Marshal.WriteIntPtr(InvokableSingleClass.Class, 0x1a0, TypedInvoke.Pointer);
            Marshal.WriteIntPtr(CachedSingleClass.Class, 0x198, CachedInvoke.MethodPointer);
            Marshal.WriteIntPtr(CachedSingleClass.Class, 0x1a0, CachedInvoke.Pointer);
            Marshal.WriteIntPtr(CustomSite2Class.Class, 0x178, CustomInvoke.MethodPointer);
            Marshal.WriteIntPtr(CustomSite2Class.Class, 0x180, CustomInvoke.Pointer);

            ActionObject = AllocateRaw(128);
            Marshal.WriteIntPtr(ActionObject, ActionSingleClass.Class);

            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies));
            Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity));
            Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName));
            Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass));
            Bind("il2cpp_object_get_class", "Ptr1", nameof(GetObjectClass));
            Bind("il2cpp_class_get_parent", "Ptr1", nameof(Parent));
            Bind("il2cpp_class_get_type", "Ptr1", nameof(ClassType));
            Bind("il2cpp_class_from_type", "Ptr1", nameof(TypeClass));
            Bind("il2cpp_class_get_name", "Ptr1", nameof(ClassName));
            Bind("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace));
            Bind("il2cpp_class_get_image", "Ptr1", nameof(ClassImage));
            Bind("il2cpp_class_instance_size", "ArraySize", nameof(InstanceSize));
            Bind("il2cpp_class_is_generic", "Predicate", nameof(Generic));
            Bind("il2cpp_class_is_inflated", "Predicate", nameof(Inflated));
            Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
            Bind("il2cpp_type_get_type", "TypeCode", nameof(TypeCode));
            Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef));
            Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType));
            Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum));
            Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize));
            Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
            Bind("il2cpp_class_get_fields", "Ptr2", nameof(Fields));
            Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField));
            Bind("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent));
            Bind("il2cpp_field_get_name", "Ptr1", nameof(FieldName));
            Bind("il2cpp_field_get_type", "Ptr1", nameof(FieldType));
            Bind("il2cpp_field_get_flags", "Flags", nameof(FieldFlags));
            Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset));
            Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField));
            Bind("il2cpp_class_get_methods", "Ptr2", nameof(Methods));
            Bind("il2cpp_method_get_name", "Ptr1", nameof(MethodName));
            Bind("il2cpp_method_get_class", "Ptr1", nameof(MethodClass));
            Bind("il2cpp_method_get_return_type", "Ptr1", nameof(MethodReturn));
            Bind("il2cpp_method_get_flags", "MethodFlags", nameof(MethodFlags));
            Bind("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount));
            Bind("il2cpp_method_get_param", "ParameterType", nameof(ParameterType));
            Bind("il2cpp_method_is_generic", "Predicate", nameof(IsGenericMethod));
            Bind("il2cpp_method_is_inflated", "Predicate", nameof(IsInflatedMethod));
            Bind("il2cpp_object_get_virtual_method", "Ptr2", nameof(VirtualMethod));
            Bind("il2cpp_object_get_size", "ArraySize", nameof(ObjectSize));
            Bind("il2cpp_class_get_rank", "Flags", nameof(Rank));
            Bind("il2cpp_class_get_element_class", "Ptr1", nameof(ElementClass));
            Bind("il2cpp_array_element_size", "ArrayElementSize", nameof(ElementSize));
            Bind("il2cpp_array_length", "ArraySize", nameof(ArrayLength));
            Bind("il2cpp_array_get_byte_length", "ArraySize", nameof(ArrayBytes));
            Bind("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(HeaderSize));
            Bind("il2cpp_object_unbox", "Ptr1", nameof(Unbox));

            Api = new NativeRuntime(name => _exports.TryGetValue(name, out var value)
                ? Marshal.GetFunctionPointerForDelegate(value)
                : Base.ExportForListenerTests(name));
            Api.BindCurrentThread();
        }

        internal void NoGameCalls() => Check(Base.Invocations == 0 && Base.ObjectFieldWrites == 0 && Base.ValueFieldWrites == 0);

        internal IntPtr CreateTypedInvokable(IntPtr actionDelegate)
        {
            var ptr = AllocateRaw(32);
            Marshal.WriteIntPtr(ptr, InvokableSingleClass.Class);
            Marshal.WriteIntPtr(ptr, 0x10, actionDelegate);
            return ptr;
        }

        internal IntPtr CreateCachedInvokable(IntPtr actionDelegate, uint argBits)
        {
            var ptr = AllocateRaw(32);
            Marshal.WriteIntPtr(ptr, CachedSingleClass.Class);
            Marshal.WriteIntPtr(ptr, 0x10, actionDelegate);
            Marshal.WriteInt32(ptr, 0x18, unchecked((int)argBits));
            return ptr;
        }

        internal IntPtr CreatePlainInvokable(IntPtr actionDelegate)
        {
            var ptr = AllocateRaw(32);
            Marshal.WriteIntPtr(ptr, PlainInvokableClass.Class);
            Marshal.WriteIntPtr(ptr, 0x10, actionDelegate);
            return ptr;
        }

        internal IntPtr CreateCustomSite2Invokable()
        {
            var ptr = AllocateRaw(32);
            Marshal.WriteIntPtr(ptr, CustomSite2Class.Class);
            return ptr;
        }

        internal IntPtr CreateBoxedSingle(uint rawBits)
        {
            var ptr = AllocateRaw(24);
            Marshal.WriteIntPtr(ptr, SingleClass.Class);
            Marshal.WriteInt32(ptr, 0x10, unchecked((int)rawBits));
            return ptr;
        }

        internal IntPtr CreateBoxedInt32(int value)
        {
            var ptr = AllocateRaw(24);
            Marshal.WriteIntPtr(ptr, Int32Class.Class);
            Marshal.WriteInt32(ptr, 0x10, value);
            return ptr;
        }

        internal IntPtr CreateObjectArray(params IntPtr[] items)
        {
            var total = 32 + items.Length * 8;
            var ptr = AllocateRaw(total);
            Marshal.WriteIntPtr(ptr, ObjectArrayClass.Class);
            _arrayLengths[ptr] = (uint)items.Length;
            _arrayObjectSizes[ptr] = (uint)total;
            for (var i = 0; i < items.Length; i++)
                Marshal.WriteIntPtr(ptr, 32 + i * 8, items[i]);
            return ptr;
        }

        private IntPtr CreateEmptyList(TypeRow listClass, TypeRow arrayClass)
            => CreateList(listClass, arrayClass);

        internal IntPtr CreateList(TypeRow listClass, TypeRow arrayClass, params IntPtr[] items)
        {
            var total = 32 + items.Length * 8;
            var arr = AllocateRaw(total);
            Marshal.WriteIntPtr(arr, arrayClass.Class);
            _arrayLengths[arr] = (uint)items.Length;
            _arrayObjectSizes[arr] = (uint)total;
            for (var i = 0; i < items.Length; i++)
                Marshal.WriteIntPtr(arr, 32 + i * 8, items[i]);
            var list = AllocateRaw(32);
            Marshal.WriteIntPtr(list, listClass.Class);
            Marshal.WriteIntPtr(list, 0x10, arr);
            Marshal.WriteInt32(list, 0x18, items.Length);
            Marshal.WriteInt32(list, 0x1c, 1);
            return list;
        }

        internal (IntPtr Event, IntPtr Calls, IntPtr Group, IntPtr PersistList, IntPtr RuntimeList, IntPtr ExecList, IntPtr GroupList) CreateEmptySingleEventGraph()
        {
            var groupList = CreateEmptyList(PersistentListClass, PersistentCallArrayClass);
            var persistList = CreateEmptyList(BaseListClass, BaseCallArrayClass);
            var runtimeList = CreateEmptyList(BaseListClass, BaseCallArrayClass);
            var execList = CreateEmptyList(BaseListClass, BaseCallArrayClass);
            var group = AllocateRaw(32);
            Marshal.WriteIntPtr(group, GroupClass.Class);
            Marshal.WriteIntPtr(group, 0x10, groupList);
            var calls = AllocateRaw(48);
            Marshal.WriteIntPtr(calls, CallsClass.Class);
            Marshal.WriteIntPtr(calls, 0x10, persistList);
            Marshal.WriteIntPtr(calls, 0x18, runtimeList);
            Marshal.WriteIntPtr(calls, 0x20, execList);
            Marshal.WriteByte(calls, 0x28, 0);
            var evt = AllocateRaw(48);
            Marshal.WriteIntPtr(evt, SingleEventClass.Class);
            Marshal.WriteIntPtr(evt, 0x10, calls);
            Marshal.WriteIntPtr(evt, 0x18, group);
            Marshal.WriteByte(evt, 0x20, 0);
            return (evt, calls, group, persistList, runtimeList, execList, groupList);
        }

        internal NativeTweenSingleEventObservation Observation(NativeTweenSingleEventStage stage, uint site,
            IntPtr invokable, IntPtr actualTarget, IntPtr argR8, uint valueBits, IntPtr rax,
            IntPtr argRdx = default, uint? xmm1Low = null)
        {
            var f = new NativeTweenSingleEventFrame {
                Version = 1, Size = 200, Stage = stage, Site = site, ThreadId = 19,
                Epoch = 5, ParentEpoch = 7, ParentSequence = 31, ParentManagedToken = 67,
                Event = new(101), List = new(201), Invokable = invokable, ActualTarget = actualTarget,
                ArgRcx = invokable, ArgRdx = argRdx, ArgR8 = argR8,
                ParentMethod = new(111), ParentCaller = new(121),
                SourceReturnPc = new(0x110d), TransportReturnPc = new(0x110d),
                SourceRsp = 0x10000, Index = 0, ValueBits = valueBits, ChildSequence = 91
            };
            var m = new NativeEventMidFrame {
                Version = 1, Size = 432, EntryRsp = 0xfff8, Rsp = 0x10000, ReturnPc = f.TransportReturnPc,
                Rax = unchecked((ulong)rax.ToInt64()), Rbp = (ulong)f.Event.ToInt64(),
                R15 = (ulong)f.List.ToInt64(), R13 = (ulong)f.ParentMethod.ToInt64(),
                Rcx = (ulong)f.ArgRcx.ToInt64(), Rdx = (ulong)f.ArgRdx.ToInt64(), R8 = (ulong)f.ArgR8.ToInt64(),
                R14 = (ulong)f.Invokable.ToInt64(),
                Xmm1 = new() { Low = xmm1Low ?? valueBits }, Xmm6 = new() { Low = valueBits }
            };
            return new(f, m);
        }

        internal NativeRuntime.RootHandle Root(IntPtr value)
        {
            BeforePublishedRoot?.Invoke(value);
            if (Retained.TryGetValue(value, out var existing)) return existing;
            var root = Api.Root(value);
            Retained.Add(value, root);
            return root;
        }

        internal NativeRuntime.RootHandle CreateFakeUnrootedHandle(IntPtr value)
            => new(999999u, _ => { }, _ => value, Api.BindCurrentThread);

        internal IntPtr AllocateRaw(int size)
        {
            var ptr = Marshal.AllocHGlobal(size);
            _allocations.Add(ptr);
            Marshal.Copy(new byte[size], 0, ptr, size);
            return ptr;
        }

        internal TypeRow AddType(string name, string ns, string shortName, int code, bool value, int size, IntPtr image, IntPtr parent = default)
        {
            var row = new TypeRow { Name = name, Namespace = ns, ShortName = shortName, Code = code, Value = value, Size = size,
                Image = image, Parent = parent, Type = AllocateRaw(16), Class = AllocateRaw(0x200), Inflated = code == 21 };
            _types.Add(row.Type, row); _classes.Add(row.Class, row); _definitions.TryAdd((image, ns, shortName), row.Class);
            return row;
        }

        private FieldRow AddField(IntPtr cls, string name, IntPtr type, int offset)
        {
            var row = new FieldRow { Pointer = AllocateRaw(16), Class = cls, Name = name, Type = type, Offset = offset };
            _fields.Add(row);
            return row;
        }

        internal MethodRow AddMethod(IntPtr cls, string name, IntPtr ret, IntPtr[] parameters, IntPtr methodPtr, uint flags, ushort slot)
        {
            var namePtr = Marshal.StringToHGlobalAnsi(name);
            _allocations.Add(namePtr);
            var paramPtr = IntPtr.Zero;
            if (parameters.Length > 0)
            {
                paramPtr = AllocateRaw(parameters.Length * IntPtr.Size);
                for (var i = 0; i < parameters.Length; i++) Marshal.WriteIntPtr(paramPtr, i * IntPtr.Size, parameters[i]);
            }
            var row = new MethodRow {
                Pointer = AllocateRaw(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()),
                Name = name, NamePtr = namePtr, Class = cls, Return = ret,
                Parameters = parameters, ParametersPtr = paramPtr, MethodPointer = methodPtr, Flags = flags, Slot = slot
            };
            WriteMethodInfo(row);
            _methods.Add(row);
            return row;
        }

        internal void UpdateMethodPointer(MethodRow row, IntPtr methodPtr) { row.MethodPointer = methodPtr; WriteMethodInfo(row); }
        internal void UpdateMethodFlags(MethodRow row, uint flags) { row.Flags = flags; WriteMethodInfo(row); }
        internal void UpdateMethodSlot(MethodRow row, ushort slot) { row.Slot = slot; WriteMethodInfo(row); }

        private static void WriteMethodInfo(MethodRow row)
        {
            var info = new HookTargetValidation.MethodInfo2022 {
                MethodPointer = row.MethodPointer, Name = row.NamePtr, Class = row.Class,
                ReturnType = row.Return, Parameters = row.ParametersPtr,
                Flags = (ushort)row.Flags, ImplementationFlags = (ushort)row.Implementation,
                Slot = row.Slot, ParameterCount = (byte)row.Parameters.Length
            };
            Marshal.StructureToPtr(info, row.Pointer, false);
        }

        private readonly Dictionary<IntPtr, uint> _arrayLengths = new(), _arrayObjectSizes = new();
        private IntPtr Text(string value) { var p = Marshal.StringToHGlobalAnsi(value); _allocations.Add(p); return p; }
        private void Bind(string name, string type, string method)
            => _exports[name] = Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this,
                GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!);
        private void CheckRooted(IntPtr obj) { if (obj != IntPtr.Zero && !Base.SingleRooted(obj)) UnrootedObjectReads.Add(obj); }
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { Marshal.WriteIntPtr(size, new IntPtr(3)); return _assemblies; }
        private IntPtr Identity(IntPtr value) => value;
        private IntPtr ImageName(IntPtr image) => Text(image == SystemImage ? "mscorlib.dll" : image == GameImage ? "Assembly-CSharp.dll" : "UnityEngine.CoreModule.dll");
        private IntPtr FindClass(IntPtr image, string ns, string name) => _definitions.GetValueOrDefault((image, ns, name));
        private IntPtr GetObjectClass(IntPtr value) { CheckRooted(value); return Marshal.ReadIntPtr(value); }
        private IntPtr Parent(IntPtr cls) => _classes[cls].Parent;
        private IntPtr ClassType(IntPtr cls) => _classes[cls].Type;
        private IntPtr TypeClass(IntPtr type) => _types[type].Class;
        private IntPtr ClassName(IntPtr cls) => Text(_classes[cls].ShortName);
        private IntPtr ClassNamespace(IntPtr cls) => Text(_classes[cls].Namespace);
        private IntPtr ClassImage(IntPtr cls) => _classes[cls].Image;
        private uint InstanceSize(IntPtr cls) => _classes[cls].InstanceSize;
        private bool Generic(IntPtr cls) => _classes[cls].Generic;
        private bool Inflated(IntPtr cls) => _classes[cls].Inflated;
        private IntPtr TypeName(IntPtr type) => Marshal.StringToCoTaskMemUTF8(_types[type].Name);
        private int TypeCode(IntPtr type) => _types[type].Code;
        private bool ByRef(IntPtr type) => _types[type].ByRef;
        private bool ValueType(IntPtr cls) => _classes[cls].Value;
        private bool IsEnum(IntPtr cls) => false;
        private int ValueSize(IntPtr cls, out uint alignment) { alignment = 1; return _classes[cls].Size; }
        private bool Assignable(IntPtr expected, IntPtr actual)
        { for (var count = 0; actual != IntPtr.Zero && count < 32; ++count, actual = _classes[actual].Parent) if (actual == expected) return true; return false; }
        private IntPtr Fields(IntPtr cls, IntPtr iterator)
        {
            var rows = _fields.Where(row => row.Class == cls).ToArray();
            var index = Marshal.ReadIntPtr(iterator).ToInt32();
            if (index >= rows.Length) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(index + 1));
            return rows[index].Pointer;
        }
        private IntPtr FindField(IntPtr cls, string name)
        {
            for (var count = 0; cls != IntPtr.Zero && count < 32; ++count, cls = _classes[cls].Parent)
            { var row = _fields.FirstOrDefault(field => field.Class == cls && field.Name == name); if (row is not null) return row.Pointer; }
            return IntPtr.Zero;
        }
        private FieldRow FieldRowOf(IntPtr field) => _fields.Single(row => row.Pointer == field);
        private IntPtr FieldParent(IntPtr field) => FieldRowOf(field).Class;
        private IntPtr FieldName(IntPtr field) => Text(FieldRowOf(field).Name);
        private IntPtr FieldType(IntPtr field) => FieldRowOf(field).Type;
        private uint FieldFlags(IntPtr field) => FieldRowOf(field).Flags;
        private int FieldOffset(IntPtr field) => FieldRowOf(field).Offset;
        private void ReadField(IntPtr owner, IntPtr field, IntPtr output)
        {
            CheckRooted(owner);
            ++FieldReads;
            var row = FieldRowOf(field);
            var bytes = new byte[_types[row.Type].Size];
            Marshal.Copy(IntPtr.Add(owner, row.Offset), bytes, 0, bytes.Length);
            Marshal.Copy(bytes, 0, output, bytes.Length);
            AfterFieldRead?.Invoke(row);
        }
        private MethodRow MethodRowOf(IntPtr method) => _methods.Single(row => row.Pointer == method);
        private IntPtr Methods(IntPtr cls, IntPtr iterator)
        {
            var rows = _methods.Where(row => row.Class == cls).ToArray();
            var index = Marshal.ReadIntPtr(iterator).ToInt32();
            if (index >= rows.Length) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(index + 1));
            return rows[index].Pointer;
        }
        private IntPtr MethodName(IntPtr method) => MethodRowOf(method).NamePtr;
        private IntPtr MethodClass(IntPtr method) => MethodRowOf(method).Class;
        private IntPtr MethodReturn(IntPtr method) => MethodRowOf(method).Return;
        private uint MethodFlags(IntPtr method, out uint implementation) { var r = MethodRowOf(method); implementation = r.Implementation; return r.Flags; }
        private uint ParameterCount(IntPtr method) => (uint)MethodRowOf(method).Parameters.Length;
        private IntPtr ParameterType(IntPtr method, uint index) => MethodRowOf(method).Parameters[index];
        private bool IsGenericMethod(IntPtr method) => MethodRowOf(method).Generic;
        private bool IsInflatedMethod(IntPtr method) => MethodRowOf(method).Inflated;
        private IntPtr VirtualMethod(IntPtr obj, IntPtr declared)
        {
            CheckRooted(obj);
            if (VirtualResolutionOverride is { } forced) return forced;
            var actualCls = Marshal.ReadIntPtr(obj);
            var decl = MethodRowOf(declared);
            for (var c = actualCls; c != IntPtr.Zero; c = _classes[c].Parent)
            {
                var found = _methods.FirstOrDefault(m => m.Class == c && m.Name == decl.Name && m.Parameters.SequenceEqual(decl.Parameters));
                if (found is not null) return found.Pointer;
            }
            return IntPtr.Zero;
        }
        private uint ObjectSize(IntPtr value)
        {
            CheckRooted(value);
            return _arrayObjectSizes.TryGetValue(value, out var size) ? size : 32u;
        }
        private uint Rank(IntPtr cls) => _classes[cls].Rank;
        private IntPtr ElementClass(IntPtr cls) => _classes[cls].Element;
        private int ElementSize(IntPtr cls) => 8;
        private uint ArrayLength(IntPtr array) { CheckRooted(array); return _arrayLengths.GetValueOrDefault(array); }
        private uint ArrayBytes(IntPtr array) { CheckRooted(array); return _arrayLengths.GetValueOrDefault(array) * 8u; }
        private uint HeaderSize() => 32;
        private IntPtr Unbox(IntPtr obj) { CheckRooted(obj); return IntPtr.Add(obj, 0x10); }

        public void Dispose()
        {
            foreach (var root in Retained.Values) root.Dispose();
            _image.Dispose();
            foreach (var ptr in _allocations) Marshal.FreeHGlobal(ptr);
            Base.Dispose();
            GC.KeepAlive(_exports);
        }

        private sealed class SyntheticPeImage : IDisposable
        {
            private readonly string _path = Path.Combine(Path.GetTempPath(), "RizToolbox.SingleDispatch." + Guid.NewGuid().ToString("N") + ".dll");
            private readonly FileStream _file;
            private readonly IntPtr _mapping;
            internal readonly IntPtr Address;
            internal SyntheticPeImage()
            {
                var bytes = new byte[0x400];
                using (var writer = new BinaryWriter(new MemoryStream(bytes)))
                {
                    writer.Write((ushort)0x5A4D); writer.BaseStream.Position = 0x3C; writer.Write(0x80); writer.BaseStream.Position = 0x80;
                    writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write((ushort)0xF0); writer.Write((ushort)0x2022);
                    var optional = writer.BaseStream.Position; writer.Write((ushort)0x20B); writer.BaseStream.Position = optional + 24; writer.Write(0x180000000UL);
                    writer.Write(0x1000); writer.Write(0x200); writer.BaseStream.Position = optional + 40; writer.Write((ushort)6); writer.Write((ushort)0);
                    writer.BaseStream.Position = optional + 48; writer.Write((ushort)6); writer.Write((ushort)0);
                    writer.BaseStream.Position = optional + 56; writer.Write(0x1570000); writer.Write(0x200); writer.BaseStream.Position = optional + 68; writer.Write((ushort)3);
                    writer.BaseStream.Position = optional + 108; writer.Write(16); writer.BaseStream.Position = optional + 0xF0;
                    writer.Write(new byte[] { 46, 116, 101, 120, 116, 0, 0, 0 }); writer.Write(0x156E000); writer.Write(0x1000); writer.Write(bytes.Length - 0x200); writer.Write(0x200);
                    writer.Write(0); writer.Write(0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0x60000020);
                }
                File.WriteAllBytes(_path, bytes);
                _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                _mapping = CreateFileMapping(_file.SafeFileHandle.DangerousGetHandle(), IntPtr.Zero, 0x1000002, 0, 0, null);
                if (_mapping == IntPtr.Zero) throw new InvalidOperationException("Synthetic PE mapping failed: " + Marshal.GetLastWin32Error());
                Address = MapViewOfFile(_mapping, 4, 0, 0, 0);
                if (Address == IntPtr.Zero) throw new InvalidOperationException("Synthetic PE view failed: " + Marshal.GetLastWin32Error());
            }
            public void Dispose() { UnmapViewOfFile(Address); CloseHandle(_mapping); _file.Dispose(); File.Delete(_path); }
            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attributes, uint protect, uint high, uint low, string? name);
            [DllImport("kernel32", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, nuint size);
            [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnmapViewOfFile(IntPtr view);
            [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
