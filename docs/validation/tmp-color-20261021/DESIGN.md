# TMP color: shared target and mutation path

Scope: extend the existing game color path to `TMPro.TMP_Text.color`. This is an offline implementation and managed validation task. It does not establish native game acceptance or completion of B13.

## Native evidence

Local source: `C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs`.

| Dump line | Declaration | RVA |
| --- | --- | --- |
| 117282 | `TMPro.TextMeshProUGUI : TMP_Text` | — |
| 120131 | `TMPro.TMP_Text : MaskableGraphic` | — |
| 120378 | `UnityEngine.Color TMP_Text.get_color()` | `0x14E3610` |
| 120379 | `System.Void TMP_Text.set_color(UnityEngine.Color)` | `0x14E4890` |

Runtime binding uses assembly `Unity.TextMeshPro.dll`, namespace `TMPro`, and exact declaring class `TMP_Text`. Resolve exact parameter lists and check declaring class, method name, and return type before retaining the binding. A metadata lookup that falls back to `Graphic.color` is refused. Missing or changed optional TMP signatures exclude TMP discovery; Image remains independently available.

## Extension

`NativeGameColorBackend` discovers live, active receivers in valid loaded scenes and excludes framework roots. Image and TMP receiver scans share the existing fixed discovery and target budgets. Receiver identity uses instance ID and native identity, so repeated discovery paths cannot create independent color channels for one receiver. Public keys never contain the address. A TMP alias is `game.ui/tmp-text/{instanceId}/color`. Names are descriptive metadata only.

Each TMP receiver supplies one `GameColorCandidate` to the existing `GameResourceService`. The service registers an opaque canonical key and a fresh `TargetHandle<ResourceColor>` generation in the existing `SemanticTargetRegistry`, then registers property `color` in the existing `MutationCoordinator` with `PriorityOverride` and `AdoptAndReapply`. Both the semantic and legacy APIs use this channel. TMP metadata is `Role=tmp-text-color`, `ResourceId=unity.tmp-text.color`; Image remains `image-color` / `unity.image.color`.

Image color, TMP color, sprite, font, and material slots keep independent baselines and leases. TMP color never reads or writes `fontSharedMaterial`, `Graphic.material`, shader parameters, or font assets. Existing unknown material/shader rejection is unaffected.

The existing channel obtains its baseline from the receiver getter, verifies each successful setter using the same getter, adopts intervening game writes, and restores the latest owned baseline when the final holder ends. Inactive live receivers restore owned values before removal; dead or unloaded receivers invalidate leases without writes. Reappearing receivers receive a fresh canonical key and generation; old leases never retarget a replacement.

## Acceptance boundary

Managed cases must cover exact metadata acceptance and inherited fallback rejection, distinguish TMP from Image target kinds, independent target baselines, two scoped plugin owners on TMP, latest external baseline adoption, setter/readback failure, target destruction/rebuild, and inactive receiver release. Existing color integration checks remain in place.

Native acceptance still requires a separately captured real TMP target, before/after getter values, two actual plugin holders, holder release order, failure preservation, disable restoration, scene invalidation, and final baseline readback. A managed fake backend or a successful build is not that receipt.
