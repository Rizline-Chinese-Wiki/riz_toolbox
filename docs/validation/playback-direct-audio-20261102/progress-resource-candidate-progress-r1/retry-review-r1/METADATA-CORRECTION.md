# Follow-up: current declared-method metadata correction

This is a separate implementation assignment received after the read-only resource retry review. The r1 source snapshots and F-01 evidence in this directory remain unchanged.

## Observed failure

The independent `metadata-r1/metadata.stderr.log` records a fresh build followed by a metadata regression result of 146/147. Its failed case is `issued stock receipt rejects resource setter is missing from its declaring class` in `source/tests/RizToolbox.Gameplay.Tests/NativePlaybackStockMetadataChecks.cs:179`.

After issuing stock metadata, the controlled fixture moves ProgressClip's current declaration enumeration from Progress to Source, while its MethodInfo and exported declaring class still identify Progress. The original `InspectDeclaredMethod` called `Method`, which returned the previously cached signature and did not re-enumerate Progress. The issued receipt therefore continued to verify. The same mutation before issuance was correctly refused.

## Correction

Only `source/src/RizToolbox.Runtime/NativeRuntime.MetadataDiagnostics.cs` was edited. Both exact declared-method inspectors now call a private `FindCurrentDeclaredMetadataMethod` helper. It freshly enumerates only the specified declaring class, matches name, static/instance shape, exact parameter count, and every parameter name, and requires exactly one current match. Existing declaring-class, return-type, MethodInfo layout, RVA, generic, ABI, and stock reference checks still run afterward. Ordinary `Method` invocation caching was not changed.

Post-edit SHA-256: `184e955233e0cc9b777f993517708be30b5a218661cbb67aa737bd8c0ee269cb`.

The reviewer stopped writing after this edit and notified the independent metadata agent to freeze and run metadata-r2, including its new duplicate-resource-setter cases before and after issuance. No build or test pass is claimed in this document; metadata-r1's original failure evidence is preserved.
