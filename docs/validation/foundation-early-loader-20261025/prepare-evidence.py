"""Prepare this batch's evidence procedures without changing earlier artifacts."""
from pathlib import Path

here = Path(__file__).resolve().parent
before = here.parent / "foundation-engine-binding-20261025"
for name in ("summarize-build.py", "freeze-before-build.py", "freeze-source.py", "freeze-package.py",
             "verify-frozen-inputs.py", "compare-sources.py", "run-build.ps1"):
    destination = here / name
    if destination.exists():
        continue
    body = (before / name).read_text(encoding="utf-8-sig")
    body = body.replace("engine-binding-mask-final-20261025", "early-loader-final-20261025")
    if name in ("freeze-before-build.py", "summarize-build.py"):
        needle = 'paths.append(repo/"native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md")' if name.startswith("freeze-") else None
        if needle:
            assert needle in body
            body = body.replace(needle, needle + '\npaths.append(repo/"docs/validation/foundation-early-loader-20261025/verify-early-loader.py")')
        else:
            # The completed local copy also includes the early-loader strict
            # evidence checks and the explicitly declared Python verifier.
            # An absent file must be prepared by this batch's maintained copy.
            raise RuntimeError("Use this batch's maintained summarize-build.py; do not regenerate its added checks from the previous batch")
    if name == "compare-sources.py":
        body = body.replace('"foundation-engine-node-20261025"', '"foundation-engine-binding-20261025"')
        body = body.replace('"operation-preinit/"', '"engine-binding/"').replace('"engine-node/"', '"early-loader/"')
        body = body.replace('changes-from-engine-node', 'changes-from-engine-binding')
    destination.write_text(body, encoding="utf-8", newline="\n")
print("Prepared new-batch procedures; previous completed archives remain unchanged.")
