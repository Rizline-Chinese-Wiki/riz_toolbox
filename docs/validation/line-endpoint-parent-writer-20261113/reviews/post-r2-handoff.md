# Post-r2 endpoint writer handoff

The latest candidate is frozen for root integration. It selects **31 endpoint writer cases**; this revision has not yet been built or tested. The previous root-r2 revision passed its build with W0/E0 and all 29 then-selected cases. Exact current and prior hashes are recorded in `post-r2-handoff.json` and `r2-before/`.

The runtime's SetParent inspector now checks Boolean's exported class image, namespace and name afresh, alongside its pointer, type code, byref status and one-byte fresh/cached ABI. It reuses the guarded canonical Void checker. The position profile text now describes its local getter, local setter and SetParent wrapper evidence. The pinned 96-byte SetParent body and hash are unchanged.

Two synthetic cases were added. One warms the metadata cache and independently drifts Boolean/ Void class image, name and namespace while preserving their class pointers and type names. The other changes one parent between the endpoint samples and the first parent-graph read; preparation must reject the disagreement before any setter. The Commands owner now performs that cross-check before accepting the graph baseline.

The remaining review limits are the unclosed Unity engine effects after an entered setter and the planner's expensive worst-case full graph checks near its declared bounds. No real game module or Unity method was exercised by this review. Root owns fresh main-tree verification.
