using RizToolbox.Adapter.GameCamera;
using RizToolbox.Sdk;
using static CameraChecks;

internal static class ScaleCameraChecks
{
    private static CameraFixture Fixture() => new(new CameraBackend {
        DeclaredProperties = new[] { NativeGameCameraBackend.LocalX, NativeGameCameraBackend.LocalScaleX }
    });
    private static CameraSource Scale(CameraFixture fixture, string id = "scale")
    {
        var source = fixture.Add(id); source.Property = NativeGameCameraBackend.LocalScaleX;
        source.Physical = "rig/" + source.Property.Property;
        source.Original = source.X = 1;
        return source;
    }

    internal static (string Name, Action Run)[] Cases => new (string, Action)[] {
        ("Scale metadata declares an independent dimensionless scalar with no invented numeric range", () => {
            using var f = Fixture(); var p = f.Plugin();
            var properties = Need(p.Camera!.GetSupportedProperties()); Equal(2, properties.Count);
            var scale = properties.Single(value => value.Role == GameCameraRoles.RigLocalScaleX);
            Equal("local-scale-x", scale.Property); Equal("dimensionless", scale.Unit);
            Check(scale.Minimum is null && scale.Maximum is null && scale.Experimental);
            Equal(ExternalWritePolicy.AdoptAndReapply, scale.ExternalWrites);
            Check(scale.Evidence.Contains("gameplay", StringComparison.OrdinalIgnoreCase));
            Equal(0, Need(p.Camera.GetTargets()).Count);
        }),
        ("Position and scale on one rig expose independent aliases and arbitration channels", () => {
            using var f = Fixture(); var position = f.Add("position"); position.Physical = "rig/local-position-x";
            var scale = Scale(f); Equal(2, f.Service.Refresh(true));
            var low = f.Plugin("scale.low"); var high = f.Plugin("scale.high");
            using var move = Need(low.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 5)));
            using var resized = Need(low.Camera.OverrideScalar(new(new(Key: scale.Alias), "run", 1.5f, 10)));
            var scaleTarget = f.Resolve(scale.Alias);
            using var winner = NeedOp(high.Mutations!.Override(new MutationKey<float>(f.Resolve(scale.SecondAlias), "local-scale-x"), 2, 20));
            Equal(scaleTarget, f.Resolve(scale.SecondAlias));
            Equal(2, NeedOp(low.Mutations!.Describe(new MutationKey<float>(scaleTarget, "local-scale-x"))).Holders.Count);
            Equal(5f, position.X); Equal(2f, scale.X);
            Check(f.Resolve(position.Alias) != scaleTarget);
            f.Host.Disable("scale.high"); Equal(1.5f, scale.X); Equal(5f, position.X);
            resized.Dispose(); Equal(1f, scale.X); Equal(5f, position.X); Check(move.IsActive);
        }),
        ("Scale restoration adopts external X and preserves fresh Y/Z without moving the position channel", () => {
            using var f = Fixture(); var position = f.Add("position"); var scale = Scale(f);
            Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
            using var move = Need(p.Camera!.OverrideScalar(new(new(Key: position.Alias), "run", 9)));
            var lease = Need(p.Camera.OverrideScalar(new(new(Key: scale.Alias), "run", 1.5f)));
            scale.X = 7; scale.Y = 23; scale.Z = 29; f.Service.Refresh(); Equal(1.5f, scale.X);
            lease.Dispose(); Equal(7f, scale.X); Equal(23f, scale.Y); Equal(29f, scale.Z); Equal(9f, position.X);
        }),
        ("Scale retirement and replacement preserve the live position graph and expire the old scale lease", () => {
            using var f = Fixture(); var position = f.Add("position"); var scale = Scale(f);
            Equal(2, f.Service.Refresh(true)); var p = f.Plugin();
            var move = Need(p.Camera!.OverrideScalar(new(new(Key: position.Alias), "run", 4)));
            var previous = Need(p.Camera.OverrideScalar(new(new(Key: scale.Alias), "run", 1.5f)));
            var handle = f.Resolve(scale.Alias); var before = scale.Writes; scale.Alive = false;
            Equal(1, f.Service.Refresh(true)); Equal(before, scale.Writes); Equal(0, scale.Outstanding);
            Equal(1, position.Outstanding); Check(move.SetValue(6).Success); Equal(6f, position.X);
            var replacement = Scale(f); Equal(2, f.Service.Refresh(true));
            Check(f.Resolve(replacement.Alias).Generation > handle.Generation && !previous.IsActive);
            Check(!previous.SetValue(8).Success); Equal(0, replacement.Writes);
            using var next = Need(p.Camera.OverrideScalar(new(new(Key: replacement.SecondAlias), "run", 1.75f)));
            Equal(1.75f, replacement.X); Equal(6f, position.X);
        }),
        ("Only complete declared scalar descriptors can publish a camera target", () => {
            using var f = Fixture(); var scale = Scale(f);
            scale.Property = NativeGameCameraBackend.LocalScaleX with { Unit = "unverified-unit" };
            Equal(0, f.Service.Refresh(true)); Equal(0, scale.Outstanding);
            scale.Property = NativeGameCameraBackend.LocalScaleX; Equal(1, f.Service.Refresh(true));
            var p = f.Plugin(); using var lease = Need(p.Camera!.OverrideScalar(new(new(Key: scale.Alias), "run", 1.5f)));
            Equal(1.5f, scale.X);
        }),
        ("Published camera property contracts stay fixed when the discovery provider changes its declarations", () => {
            using var f = Fixture(); var p = f.Plugin(); var original = Need(p.Camera!.GetSupportedProperties());
            f.Backend.DeclaredProperties = new[] { NativeGameCameraBackend.LocalScaleX with { Property = "invented-scale" } };
            var scale = Scale(f); Equal(1, f.Service.Refresh(true));
            Equal(2, Need(p.Camera.GetSupportedProperties()).Count); Check(original.SequenceEqual(Need(p.Camera.GetSupportedProperties())));
            using var lease = Need(p.Camera.OverrideScalar(new(new(Key: scale.Alias), "run", 1.5f)));
            Equal(1.5f, scale.X);
        })
    };
}
