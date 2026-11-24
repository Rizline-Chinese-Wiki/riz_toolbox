using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter.GameCamera;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static CameraChecks;

internal static class NativeCameraChecks
{
    internal static (string Name, Action Run)[] Cases => new (string, Action)[] {
        ("Actual NativeRuntime Vector3 accessors preserve freshly read Y/Z while changing only X", () => {
            using var native = Native(); var api = native.CameraApi;
            using (var rig = api.Root(native.CameraRigTransform))
            {
                var accessor = NativeCameraPositionAccessors.Bind(api, native.CameraTransformClass);
                native.CameraPosition = new(.5f, 17, 23); accessor.WriteX(api, rig, -100000, () => { });
                Equal(new NativeVector3(-100000, 17, 23), accessor.ReadPosition(api, rig)); Equal(1, native.CameraSets);
                Check(native.AllCameraReceiversRooted && native.AllCameraValuesRooted); Equal(0, native.FieldWrites);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scalar scope rollback restores only X and preserves external Y/Z", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                native.CameraPosition = new(7, 31, 37); f.Service.Refresh(); Equal(new NativeVector3(2, 31, 37), native.CameraPosition);
                native.CameraPosition = new(2, 41, 43); f.Host.Disable(p.Manifest.Id);
                Equal(new NativeVector3(7, 41, 43), native.CameraPosition);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native accessor signatures reject an incorrect getter return and setter parameter before invocation", () => {
            using var native = Native(); native.CameraMethodByName("get_localPosition").Return = native.Single;
            Throws<NotSupportedException>(() => NativeCameraPositionAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
            native.CameraMethodByName("get_localPosition").Return = native.CameraVectorType;
            native.CameraMethodByName("set_localPosition").Parameters = new[] { native.Single };
            Throws<MissingMethodException>(() => NativeCameraPositionAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
        }),
        ("Actual native accessor signatures reject a nonvoid setter before invocation", () => {
            using var native = Native(); native.CameraMethodByName("set_localPosition").Return = native.Single;
            Throws<NotSupportedException>(() => NativeCameraPositionAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
        }),
        ("Actual native accessors reject a foreign receiver, expired root and worker thread before setter effects", () => {
            using var native = Native(); var api = native.CameraApi; var accessor = NativeCameraPositionAccessors.Bind(api, native.CameraTransformClass);
            using (var foreign = api.Root(native.CameraController)) Throws<NotSupportedException>(() => accessor.WriteX(api, foreign, 2, () => { }));
            var expired = api.Root(native.CameraRigTransform); expired.Dispose(); Throws<InvalidOperationException>(() => accessor.WriteX(api, expired, 2, () => { }));
            using (var rig = api.Root(native.CameraRigTransform)) Task.Run(() => Throws<InvalidOperationException>(() => accessor.WriteX(api, rig, 2, () => { }))).GetAwaiter().GetResult();
            Equal(0, native.CameraSets); Equal(0, native.ActiveRoots);
        }),
        ("A native getter that changes run ownership cannot be followed by a stale scalar setter", () => {
            using var native = Native(); var api = native.CameraApi; var accessor = NativeCameraPositionAccessors.Bind(api, native.CameraTransformClass);
            using (var rig = api.Root(native.CameraRigTransform))
            {
                native.OnCameraRead = () => native.CameraMatches = false;
                Throws<TargetExpiredException>(() => accessor.WriteX(api, rig, 2, () => { if (!native.CameraMatches) throw new TargetExpiredException("Controlled reentry"); }));
                Equal(0, native.CameraSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Full actual native camera discovery roots controller/info/chart/progress/rig/Camera and verifies the run", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(7, native.CameraActiveSceneHandle); Equal(-12, native.CameraControllerSceneHandle);
                Check(native.CameraRigSceneHandle != native.CameraActiveSceneHandle && native.CameraViewSceneHandle != native.CameraActiveSceneHandle);
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                var target = Need(p.Camera!.GetTargets(new(Role: GameCameraRoles.RigLocalPositionX))).Single(); Equal(typeof(float), target.Target.ValueType);
                using var lease = Need(p.Camera.OverrideScalar(new(new(Key: target.Target.Key), "run", 5)));
                Equal(5f, native.CameraPosition.X); Check(native.CameraMatchCalls > 0 && native.AllCameraMatcherArgumentsRooted);
                Check(native.AllCameraReceiversRooted && native.AllCameraValuesRooted && native.AllArrayReadsRooted);
                Equal(0, native.FieldWrites);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Persistent controller, rig and Camera scenes retain current scalar leases across discovery ticks", () => {
            using var native = Native();
            native.SetCameraSceneName(native.CameraControllerSceneHandle, "Play");
            native.SetCameraSceneName(native.CameraRigSceneHandle, "RigOverlay");
            native.SetCameraSceneName(native.CameraViewSceneHandle, "CameraOverlay");
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                using var position = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                using var scale = Need(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f)));
                Equal(2, f.Service.Refresh(true)); Check(position.IsActive && scale.IsActive);
                Check(position.SetValue(3).Success && scale.SetValue(2.5f).Success);
                Equal(new NativeVector3(3, -2.4f, 0), native.CameraPosition);
                Equal(new NativeVector3(2.5f, 2, 3), native.CameraScale);
                Check(native.AllCameraMatcherArgumentsRooted && native.AllCameraReceiversRooted && native.AllCameraValuesRooted);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Camera active scene identity cannot be supplied by same-named object scenes", () => {
            using var native = Native();
            foreach (var component in new[] { native.CameraController, native.CameraRigControl, native.CameraObject })
                native.SetCameraSceneName(component == native.CameraController ? native.CameraControllerSceneHandle
                    : component == native.CameraRigControl ? native.CameraRigSceneHandle : native.CameraViewSceneHandle, "Play");
            native.CameraScene = "Menu";
            using (var f = new CameraFixture(Backend(native)))
            { Equal(0, f.Service.Refresh(true)); Equal(0, native.CameraSets); Equal(0, native.CameraScaleSets); }
            Equal(0, native.ActiveRoots);
        }),
        ("Camera selectively rejects invalid or unloaded active, controller, rig and Camera scenes", () => {
            foreach (var which in new[] { 0, 1, 2, 3 }) foreach (var valid in new[] { false, true })
            {
                using var native = Native(); var captured = CaptureScale(native);
                using (captured.Lifetime)
                {
                    var handle = which == 0 ? native.CameraActiveSceneHandle : which == 1 ? native.CameraControllerSceneHandle
                        : which == 2 ? native.CameraRigSceneHandle : native.CameraViewSceneHandle;
                    if (valid) native.SetCameraSceneLoaded(handle, false); else native.SetCameraSceneValid(handle, false);
                    Check(!captured.Candidate.IsAlive()); Throws<TargetExpiredException>(() => captured.Candidate.Write(1.5f));
                    if (which == 1) Throws<InvalidOperationException>(() => Backend(native).Discover(new("run", "chart", "Play", 10), 64).ToArray());
                    else Throws<TargetExpiredException>(() => Backend(native).Discover(new("run", "chart", "Play", 10), 64).ToArray());
                    Equal(0, native.CameraScaleGets); Equal(0, native.CameraScaleSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Camera rejects disabled controller, rig and Camera behaviours before scalar writes", () => {
            foreach (var component in new[] { 0, 1, 2 })
            {
                using var native = Native(); var captured = CaptureScale(native);
                using (captured.Lifetime)
                {
                    native.SetCameraBehaviourEnabled(component == 0 ? native.CameraController
                        : component == 1 ? native.CameraRigControl : native.CameraObject, false);
                    Check(!captured.Candidate.IsAlive()); Throws<TargetExpiredException>(() => captured.Candidate.Write(1.5f));
                    if (component == 0) Throws<InvalidOperationException>(() => Backend(native).Discover(new("run", "chart", "Play", 10), 64).ToArray());
                    else Throws<TargetExpiredException>(() => Backend(native).Discover(new("run", "chart", "Play", 10), 64).ToArray());
                    Equal(0, native.CameraScaleGets); Equal(0, native.CameraScaleSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Independent rig and cached Camera transforms use the same current information and run graph", () => {
            using var native = Native(); native.CameraDescendant = false;
            Check(native.CameraRigTransform != native.CameraViewTransform);
            Check(native.CameraRigSceneHandle != native.CameraViewSceneHandle);
            var backend = Backend(native);
            using (var f = new CameraFixture(backend))
            {
                Equal(2, f.Service.Refresh(true));
                var p = f.Plugin();
                using var position = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                using var scale = Need(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f)));
                Equal(new NativeVector3(2, -2.4f, 0), native.CameraPosition);
                Equal(new NativeVector3(1.5f, 2, 3), native.CameraScale);
                Check(native.AllCameraMatcherArgumentsRooted && native.AllCameraReceiversRooted && native.AllCameraValuesRooted);
                Throws<TargetExpiredException>(() => backend.Discover(new("old-run", "chart", "Play", 10), 64).ToArray());
                native.SetCameraBehaviourEnabled(native.CameraObject, false);
                var positionWrites = native.CameraSets; var scaleWrites = native.CameraScaleSets;
                Check(!position.SetValue(3).Success && !scale.SetValue(2).Success);
                Equal(positionWrites, native.CameraSets); Equal(scaleWrites, native.CameraScaleSets);
                Equal(0, f.Service.Refresh(true));
                Check(!position.IsActive && !scale.IsActive);
                Check(f.Log.Messages.Any(message => message.Contains("failed check: camera.behaviour-disabled.", StringComparison.Ordinal)));
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Camera discovery names the disabled rig or Camera behaviour without changing public error code", () => {
            foreach (var rig in new[] { true, false })
            {
                using var native = Native();
                native.SetCameraBehaviourEnabled(rig ? native.CameraRigControl : native.CameraObject, false);
                var failedCheck = rig ? "rig-control.behaviour-disabled" : "camera.behaviour-disabled";
                var backend = Backend(native);
                AssertDiscoveryDiagnostic(native, backend, failedCheck);
                using (var f = new CameraFixture(backend))
                {
                    Equal(0, f.Service.Refresh(true));
                    Check(f.Log.Messages.Any(message => message.Contains("failed check: " + failedCheck + ".", StringComparison.Ordinal)));
                    var p = f.Plugin();
                    Failure(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)), ErrorCode.TargetMissing, true);
                    Equal(0, native.CameraSets); Equal(0, native.CameraScaleSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Camera discovery reports a progress flag changed after initial graph capture", () => {
            using var native = Native(); var calls = 0;
            var backend = Backend(native, (information, controller, run, scene) => {
                var accepted = native.MatchCameraRun(information, controller, run, scene);
                if (++calls == 2) native.SetCameraFlag("progress.ready", false);
                return accepted;
            });
            AssertDiscoveryDiagnostic(native, backend, "field-value.progress.ready");
            Equal(2, calls); Check(native.AllCameraMatcherArgumentsRooted);
            Equal(0, native.CameraSets); Equal(0, native.CameraScaleSets);
        }),
        ("Actual native graph rejects matcher changes immediately before another discovery tick", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                var before = native.CameraSets; native.CameraMatches = false; Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets); Check(!lease.IsActive);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Native camera discovery rejects an old run ID or scene generation", () => {
            foreach (var run in new[] { new GameCameraRun("old-run", "chart", "Play", 10), new GameCameraRun("run", "chart", "Play", 9) })
            {
                using var native = Native();
                Throws<TargetExpiredException>(() => Backend(native).Discover(run, 64).ToArray());
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Replacing actual information/chart/progress references expires old scalar leases", () => {
            foreach (var key in new[] { "controller.info", "info.chart", "info.progress" })
            {
                using var native = Native(); using (var f = new CameraFixture(Backend(native)))
                {
                    f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                    var before = native.CameraSets;
                    var original = key == "controller.info" ? native.CameraInformation : key == "info.chart" ? native.CameraChart : native.CameraProgress;
                    native.SetCameraReference(key, native.CameraReplacement(original)); Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("CameraControl and NoteJudgeControl back-references must still point to the rooted information", () => {
            foreach (var key in new[] { "rig.info", "judge.info" })
            {
                using var native = Native(); using (var f = new CameraFixture(Backend(native)))
                {
                    f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                    var before = native.CameraSets; native.SetCameraReference(key, native.CameraReplacement(native.CameraInformation));
                    Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Replacing the actual cached Camera expires its old graph before a write", () => {
            using var native = Native(); using (var f = new CameraFixture(Backend(native)))
            {
                f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                var before = native.CameraSets; native.SetCameraReference("judge.camera", native.CameraReplacement(native.CameraObject));
                Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Replacing rig or Camera transforms cannot redirect an old scalar lease", () => {
            foreach (var rig in new[] { true, false })
            {
                using var native = Native(); using (var f = new CameraFixture(Backend(native)))
                {
                    f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                    var before = native.CameraSets; native.SetCameraTransform(rig ? native.CameraRigControl : native.CameraObject, native.CameraOtherTransform);
                    Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Native active scene and nonterminal progress conditions are checked on every scalar access", () => {
            foreach (var change in new Action<FakeNative>[] { n => n.CameraActive = false,
                n => n.CameraLoaded = false, n => n.CameraValid = false, n => n.CameraScene = "Menu",
                n => n.SetCameraFlag("info.loaded", false), n => n.SetCameraFlag("info.over", true),
                n => n.SetCameraFlag("info.crashed", true), n => n.SetCameraFlag("progress.ready", false) })
            {
                using var native = Native(); using (var f = new CameraFixture(Backend(native)))
                {
                    f.Service.Refresh(true); var p = f.Plugin(); var lease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)));
                    var before = native.CameraSets; change(native); Check(!lease.SetValue(3).Success); Equal(before, native.CameraSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Native discovery refuses multiple active NormalMode controllers and releases all temporary roots", () => {
            using var native = Native(); native.SetCameraControllers(native.CameraController, native.CameraOtherController);
            using (var f = new CameraFixture(Backend(native))) { Equal(0, f.Service.Refresh(true)); Check(f.Log.Messages.Any(x => x.Contains("AmbiguousTarget"))); Equal(0, native.CameraSets); }
            Equal(0, native.ActiveRoots);
        }),
        ("A Camera on the exact rig transform is accepted without fabricating a descendant", () => {
            using var native = Native(); native.SetCameraTransform(native.CameraObject, native.CameraRigTransform); native.CameraDescendant = false;
            using (var f = new CameraFixture(Backend(native))) Equal(2, f.Service.Refresh(true));
            Equal(0, native.ActiveRoots);
        }),
        ("Nonfinite actual native position X prevents position registration while the independent scale remains available", () => {
            using var native = Native(); native.CameraPosition = new(float.NaN, -2.4f, 0);
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(1, f.Service.Refresh(true)); Equal(1, f.Targets.GetTargets().Count);
                var p = f.Plugin(); Equal(0, Need(p.Camera!.GetTargets(new(Role: GameCameraRoles.RigLocalPositionX))).Count);
                Equal(1, Need(p.Camera.GetTargets(new(Role: GameCameraRoles.RigLocalScaleX))).Count); Equal(0, native.CameraSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native setter mutation followed by an exception rolls back X without clobbering Y/Z", () => {
            using var native = Native(); using (var f = new CameraFixture(Backend(native)))
            {
                f.Service.Refresh(true); var p = f.Plugin(); using var low = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2, 10)));
                native.CameraPosition = new(2, 47, 53); native.CameraFailSetter = true;
                Failure(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 3, 20)), ErrorCode.MutationWriteFailed);
                Equal(new NativeVector3(2, 47, 53), native.CameraPosition); Check(low.IsActive);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native rendered scalar readback cannot succeed merely because a setter returned", () => {
            using var native = Native(); using (var f = new CameraFixture(Backend(native)))
            {
                f.Service.Refresh(true); var p = f.Plugin(); native.CameraIgnoreWrites = true;
                Failure(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)), ErrorCode.MutationWriteFailed);
                Equal(.5f, native.CameraPosition.X);
            }
            Equal(0, native.ActiveRoots);
        })
    }.Concat(ScaleCases).ToArray();
    private static (string Name, Action Run)[] ScaleCases => new (string, Action)[] {
        ("Actual NativeRuntime localScale X writes preserve nonuniform and freshly changed Y/Z", () => {
            using var native = Native(); var api = native.CameraApi;
            using (var rig = api.Root(native.CameraRigTransform))
            {
                var accessor = NativeCameraScaleAccessors.Bind(api, native.CameraTransformClass);
                Equal(new NativeVector3(1, 2, 3), accessor.ReadScale(api, rig));
                accessor.WriteX(api, rig, 1.5f, () => { });
                Equal(new NativeVector3(1.5f, 2, 3), accessor.ReadScale(api, rig));
                native.CameraScale = new(1.5f, 17, 23); accessor.WriteX(api, rig, -100000, () => { });
                Equal(new NativeVector3(-100000, 17, 23), accessor.ReadScale(api, rig)); Equal(2, native.CameraScaleSets);
                Equal(new NativeVector3(.5f, -2.4f, 0), native.CameraPosition); Equal(0, native.CameraSets);
                Check(native.AllCameraReceiversRooted && native.AllCameraValuesRooted); Equal(0, native.FieldWrites);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native discovery gives each exact scalar an independent complete graph lifetime", () => {
            using var native = Native(); var backend = Backend(native);
            using (var iterator = backend.Discover(new("run", "chart", "Play", 10), 64).GetEnumerator())
            {
                Check(iterator.MoveNext()); var position = iterator.Current; using var positionLifetime = position.Retain();
                Check(iterator.MoveNext()); var scale = iterator.Current; using var scaleLifetime = scale.Retain();
                Check(!iterator.MoveNext()); Equal(NativeGameCameraBackend.LocalX, position.Property); Equal(NativeGameCameraBackend.LocalScaleX, scale.Property);
                Check(position.Identity != scale.Identity && position.PhysicalIdentity != scale.PhysicalIdentity && position.CanonicalBase != scale.CanonicalBase);
                Check(!position.Aliases.Intersect(scale.Aliases, StringComparer.Ordinal).Any());
                var bothRoots = native.ActiveRoots; Check(bothRoots > 0); Equal(0, bothRoots % 2);
                positionLifetime.Dispose(); Equal(bothRoots / 2, native.ActiveRoots); Check(!position.IsAlive()); Check(scale.IsAlive());
                scale.Write(1.5f); Equal(new NativeVector3(1.5f, 2, 3), native.CameraScale); Equal(0, native.CameraSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale and position channels coexist and retiring position leaves scale rooted and writable", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                var properties = Need(p.Camera!.GetSupportedProperties()); Equal(2, properties.Count);
                var property = properties.Single(x => x.Role == GameCameraRoles.RigLocalScaleX);
                Equal("local-scale-x", property.Property); Equal("dimensionless", property.Unit);
                Check(property.Minimum is null && property.Maximum is null && property.Experimental && property.Evidence.Contains("gameplay"));
                Equal(ExternalWritePolicy.AdoptAndReapply, property.ExternalWrites);
                var position = f.Resolve("game.camera/current/local-position-x"); var scale = f.Resolve("game.camera/current/local-scale-x");
                Check(position != scale);
                using var positionLease = Need(p.Camera.OverrideScalar(new(new(Key: "game.camera/current/local-position-x"), "run", 4)));
                using var scaleLease = Need(p.Camera.OverrideScalar(new(new(Key: "game.camera/current/local-scale-x"), "run", 1.5f)));
                Equal(new NativeVector3(4, -2.4f, 0), native.CameraPosition); Equal(new NativeVector3(1.5f, 2, 3), native.CameraScale);
                positionLease.Dispose(); Equal(.5f, native.CameraPosition.X); Check(scaleLease.SetValue(2).Success);
                var bothRoots = native.ActiveRoots; Check(f.Targets.Invalidate(position).Success);
                Equal(bothRoots / 2, native.ActiveRoots); Check(!f.Targets.Acquire(position).Success);
                Check(scaleLease.SetValue(2.5f).Success); Equal(new NativeVector3(2.5f, 2, 3), native.CameraScale);
                Equal(scale, f.Resolve("game.camera/current/local-scale-x")); Equal(1, Need(p.Camera.GetTargets()).Count);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale aliases share one priority channel across two plugins and restore each external baseline", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var lowPlugin = f.Plugin("native.camera.low"); var highPlugin = f.Plugin("native.camera.high");
                var scale = f.Resolve("game.camera/current/local-scale-x"); Equal(scale, f.Resolve("game.camera/rig/101/local-scale-x"));
                Need(lowPlugin.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 4, 10)));
                Need(lowPlugin.Camera.OverrideScalar(new(new(Key: "game.camera/rig/101/local-scale-x"), "run", 1.5f, 10)));
                NeedOp(highPlugin.Mutations!.Override(new MutationKey<float>(scale, "local-scale-x"), 2.5f, 20));
                Equal(2, NeedOp(lowPlugin.Mutations!.Describe(new MutationKey<float>(scale, "local-scale-x"))).Holders.Count);
                Equal(4f, native.CameraPosition.X); Equal(2.5f, native.CameraScale.X);
                native.CameraPosition = new(9, 31, 37); native.CameraScale = new(7, 17, 23); f.Service.Refresh();
                Equal(new NativeVector3(4, 31, 37), native.CameraPosition); Equal(new NativeVector3(2.5f, 17, 23), native.CameraScale);
                f.Host.Disable("native.camera.high"); Equal(new NativeVector3(1.5f, 17, 23), native.CameraScale); Equal(4f, native.CameraPosition.X);
                native.CameraScale = new(1.5f, 41, 43); f.Host.Disable("native.camera.low");
                Equal(new NativeVector3(7, 41, 43), native.CameraScale); Equal(new NativeVector3(9, 31, 37), native.CameraPosition);
                var before = native.CameraScaleSets;
                Failure(lowPlugin.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 3)), ErrorCode.ScopeExpired);
                Equal(before, native.CameraScaleSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale metadata rejects wrong current getter return setter parameter and nonvoid setter", () => {
            foreach (var change in new Action<FakeNative>[] {
                n => n.CameraMethodByName("get_localScale").Return = n.Single,
                n => n.CameraMethodByName("set_localScale").Return = n.Single })
            {
                using var native = Native(); NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass); change(native);
                Throws<NotSupportedException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
            }
            using (var native = Native())
            {
                NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass);
                native.CameraMethodByName("set_localScale").Parameters = new[] { native.Single };
                Throws<MissingMethodException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
            }
        }),
        ("Actual native scale metadata rejects static foreign declaring generic and inflated accessors after cache warmup", () => {
            foreach (var method in new[] { "get_localScale", "set_localScale" })
            {
                foreach (var change in new Action<FakeNative.CameraMethod>[] { row => row.Static = true, row => row.Class = IntPtr.Zero })
                {
                    using var native = Native(); NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass);
                    change(native.CameraMethodByName(method));
                    Throws<MissingMethodException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
                }
                foreach (var change in new Action<FakeNative.CameraMethod>[] { row => row.Generic = true, row => row.Inflated = true })
                {
                    using var native = Native(); NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass);
                    change(native.CameraMethodByName(method));
                    Throws<NotSupportedException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
                }
            }
        }),
        ("Actual native scale bind freshly rejects duplicate or replaced MethodInfos even when lookup was cached", () => {
            foreach (var method in new[] { "get_localScale", "set_localScale" })
            {
                using (var native = Native())
                {
                    NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass); native.DuplicateCameraMethod(method);
                    Throws<NotSupportedException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
                }
                using (var native = Native())
                {
                    NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass);
                    var original = native.CameraMethodByName(method); native.DuplicateCameraMethod(method); original.Name = "retired_" + method;
                    Throws<NotSupportedException>(() => NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass)); Equal(0, native.Invocations);
                }
            }
        }),
        ("Actual native scale rejects foreign receivers expired roots workers and nonfinite requests before setter effects", () => {
            using var native = Native(); var api = native.CameraApi; var accessor = NativeCameraScaleAccessors.Bind(api, native.CameraTransformClass);
            using (var foreign = api.Root(native.CameraController)) Throws<NotSupportedException>(() => accessor.WriteX(api, foreign, 2, () => { }));
            var expired = api.Root(native.CameraRigTransform); expired.Dispose();
            Throws<InvalidOperationException>(() => accessor.WriteX(api, expired, 2, () => { }));
            using (var rig = api.Root(native.CameraRigTransform))
            {
                Task.Run(() => Throws<InvalidOperationException>(() => accessor.WriteX(api, rig, 2, () => { }))).GetAwaiter().GetResult();
                foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Throws<ArgumentOutOfRangeException>(() => accessor.WriteX(api, rig, value, () => { }));
            }
            Equal(0, native.CameraScaleSets); Equal(0, native.CameraScaleGets); Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale rejects a mismatched boxed getter result before any setter and releases temporary roots", () => {
            using var native = Native(); var api = native.CameraApi; var accessor = NativeCameraScaleAccessors.Bind(api, native.CameraTransformClass);
            native.CameraScaleBoxType = native.Single;
            using (var rig = api.Root(native.CameraRigTransform)) Throws<NotSupportedException>(() => accessor.WriteX(api, rig, 2, () => { }));
            Equal(1, native.CameraScaleGets); Equal(0, native.CameraScaleSets); Equal(0, native.ActiveRoots);
            Check(native.AllCameraReceiversRooted && native.AllCameraValuesRooted);
        }),
        ("Actual native scale graph rechecks run and rig after a reentrant getter before entering the setter", () => {
            foreach (var change in new Action<FakeNative>[] { n => n.CameraMatches = false,
                n => n.SetCameraTransform(n.CameraRigControl, n.CameraOtherTransform) })
            {
                using var native = Native(); var captured = CaptureScale(native);
                using (captured.Lifetime)
                {
                    native.OnCameraScaleRead = () => change(native);
                    Throws<TargetExpiredException>(() => captured.Candidate.Write(1.5f));
                    Equal(1, native.CameraScaleGets); Equal(0, native.CameraScaleSets); Check(!captured.Candidate.IsAlive());
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Actual native scale graph rejects every run owner link and terminal condition before setter", () => {
            foreach (var change in new Action<FakeNative>[] {
                n => n.SetCameraReference("controller.info", n.CameraReplacement(n.CameraInformation)),
                n => n.SetCameraReference("info.chart", n.CameraReplacement(n.CameraChart)),
                n => n.SetCameraReference("info.progress", n.CameraReplacement(n.CameraProgress)),
                n => n.SetCameraReference("info.rig", n.CameraReplacement(n.CameraRigControl)),
                n => n.SetCameraReference("info.judge", n.CameraReplacement(n.CameraJudge)),
                n => n.SetCameraReference("rig.info", n.CameraReplacement(n.CameraInformation)),
                n => n.SetCameraReference("judge.info", n.CameraReplacement(n.CameraInformation)),
                n => n.SetCameraReference("judge.camera", n.CameraReplacement(n.CameraObject)),
                n => n.SetCameraTransform(n.CameraObject, n.CameraOtherTransform),
                n => n.CameraActive = false, n => n.CameraLoaded = false,
                n => n.CameraValid = false, n => n.CameraScene = "Menu", n => n.SetCameraFlag("info.loaded", false),
                n => n.SetCameraFlag("info.over", true), n => n.SetCameraFlag("info.crashed", true), n => n.SetCameraFlag("progress.ready", false) })
            {
                using var native = Native(); var captured = CaptureScale(native);
                using (captured.Lifetime)
                {
                    change(native); Throws<TargetExpiredException>(() => captured.Candidate.Write(1.5f));
                    Equal(0, native.CameraScaleGets); Equal(0, native.CameraScaleSets);
                }
                Equal(0, native.ActiveRoots);
            }
        }),
        ("Actual native scale rebuild rejects old tickets and writes only through the newly captured rig graph", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                var old = f.Resolve("game.camera/current/local-scale-x");
                var oldLease = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f)));
                var before = native.CameraScaleSets; native.SetCameraTransform(native.CameraRigControl, native.CameraOtherTransform);
                Check(!oldLease.SetValue(2).Success); Equal(before, native.CameraScaleSets); Check(!oldLease.IsActive);
                Equal(2, f.Service.Refresh(true)); Check(!f.Targets.Acquire(old).Success);
                var current = f.Resolve("game.camera/current/local-scale-x"); Check(current.Generation > old.Generation);
                Check(!p.Mutations!.Override(new MutationKey<float>(old, "local-scale-x"), 2).Success); Equal(before, native.CameraScaleSets);
                using var lease = Need(p.Camera.OverrideScalar(new(new(Key: current.Key), "run", 2.5f)));
                Equal(new NativeVector3(2.5f, 2, 3), native.CameraScale); Equal(native.CameraOtherTransform, native.CameraLastScaleWriteReceiver);
                Check(native.AllCameraMatcherArgumentsRooted && native.AllCameraReceiversRooted && native.AllCameraValuesRooted); Equal(0, native.FieldWrites);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native nonfinite scale rejects only its target and leaves the independent position graph usable", () => {
            using var native = Native(); native.CameraScale = new(float.NaN, 2, 3);
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(1, f.Service.Refresh(true)); var p = f.Plugin();
                Equal(0, Need(p.Camera!.GetTargets(new(Role: GameCameraRoles.RigLocalScaleX))).Count);
                Equal(1, Need(p.Camera.GetTargets(new(Role: GameCameraRoles.RigLocalPositionX))).Count);
                using var position = Need(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 4)));
                Equal(4f, native.CameraPosition.X); Equal(0, native.CameraScaleSets);
                native.CameraScale = new(1, 2, 3); Equal(2, f.Service.Refresh(true)); Check(position.IsActive);
                using var scale = Need(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f)));
                Equal(new NativeVector3(1.5f, 2, 3), native.CameraScale);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale mutate then throw restores the prior winner and preserves fresh nonuniform Y/Z", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
                using var low = Need(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f, 10)));
                native.CameraScale = new(1.5f, 47, 53); native.CameraFailScaleSetter = true;
                Failure(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 2.5f, 20)), ErrorCode.MutationWriteFailed);
                Equal(new NativeVector3(1.5f, 47, 53), native.CameraScale); Check(low.IsActive);
                Equal(new NativeVector3(.5f, -2.4f, 0), native.CameraPosition); Equal(0, native.CameraSets);
            }
            Equal(0, native.ActiveRoots);
        }),
        ("Actual native scale readback rejects an ignored setter and worker or expired views produce zero scale writes", () => {
            using var native = Native();
            using (var f = new CameraFixture(Backend(native)))
            {
                Equal(2, f.Service.Refresh(true)); var p = f.Plugin(); native.CameraIgnoreScaleWrites = true;
                Failure(p.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 1.5f)), ErrorCode.MutationWriteFailed);
                Equal(new NativeVector3(1, 2, 3), native.CameraScale); var before = native.CameraScaleSets;
                Task.Run(() => Failure(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 2)), ErrorCode.WrongThread)).GetAwaiter().GetResult();
                Equal(before, native.CameraScaleSets); f.Host.Disable(p.Manifest.Id);
                Failure(p.Camera.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalScaleX), "run", 2)), ErrorCode.ScopeExpired); Equal(before, native.CameraScaleSets);
            }
            Equal(0, native.ActiveRoots);
        })
    };
    private static (GameCameraCandidate Candidate, IDisposable Lifetime) CaptureScale(FakeNative native)
    {
        foreach (var candidate in Backend(native).Discover(new("run", "chart", "Play", 10), 64))
            if (candidate.Property == NativeGameCameraBackend.LocalScaleX) return (candidate, candidate.Retain());
        throw new InvalidOperationException("Controlled native scale candidate is absent.");
    }
    private static FakeNative Native() { var native = new FakeNative(); native.ConfigureCamera(); return native; }

    private static void AssertDiscoveryDiagnostic(FakeNative native, NativeGameCameraBackend backend, string failedCheck)
    {
        TargetExpiredException? failure = null;
        try { backend.Discover(new("run", "chart", "Play", 10), 64).ToArray(); }
        catch (TargetExpiredException error) { failure = error; }
        var message = failure?.Message ?? throw new InvalidOperationException("Expected TargetExpiredException from camera discovery.");
        Check(message.StartsWith("TargetExpired: ", StringComparison.Ordinal));
        Check(message.EndsWith("failed check: " + failedCheck + ".", StringComparison.Ordinal));
        Equal(0, native.ActiveRoots);
    }

    // Test-only construction reaches the actual production discovery/graph implementation
    // while the shipping constructor continues to require its loaded-PE gate.
    private static NativeGameCameraBackend Backend(FakeNative native, Func<long, long, string, long, bool>? matcher = null)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var backend = (NativeGameCameraBackend)RuntimeHelpers.GetUninitializedObject(typeof(NativeGameCameraBackend));
        void Set(string name, object value) => typeof(NativeGameCameraBackend).GetField(name, flags)!.SetValue(backend, value);
        Set("_api", native.CameraApi); Set("_main", new MainThreadDispatcher());
        Set("_matchesRun", matcher ?? new Func<long, long, string, long, bool>(native.MatchCameraRun));
        Set("_fields", native.CameraFieldDescriptors()); Set("_controller", native.CameraControllerClass); Set("_transform", native.CameraTransformClass);
        foreach (var pair in new[] { ("_resourcesFind", "FindObjectsOfTypeAll"), ("_getTransform", "get_transform"),
            ("_getGo", "get_gameObject"), ("_getScene", "get_scene"), ("_getActive", "get_activeInHierarchy"),
            ("_isActiveAndEnabled", "get_isActiveAndEnabled"), ("_getActiveScene", "GetActiveScene"),
            ("_implicit", "op_Implicit"), ("_sceneValid", "IsValidInternal"), ("_sceneLoaded", "GetIsLoadedInternal"),
            ("_sceneName", "GetNameInternal"), ("_getId", "GetInstanceID") }) Set(pair.Item1, native.CameraMethodByName(pair.Item2).Pointer);
        Set("_position", NativeCameraPositionAccessors.Bind(native.CameraApi, native.CameraTransformClass));
        Set("_scale", NativeCameraScaleAccessors.Bind(native.CameraApi, native.CameraTransformClass));
        Set("<Properties>k__BackingField", Array.AsReadOnly(new[] { NativeGameCameraBackend.LocalX, NativeGameCameraBackend.LocalScaleX }));
        return backend;
    }
}
