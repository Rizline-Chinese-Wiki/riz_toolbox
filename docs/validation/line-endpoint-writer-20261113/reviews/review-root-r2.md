# Endpoint writer root r2 review

Read-only review of the repaired isolated candidate. I did not run the live 17-case controller or change candidate/main files. Exact source hashes are in `review-root-r2.json`.

The revisions address the earlier writer lifetime, single endpoint sample, one-pass vector, and metadata/root callback gaps. The source lifetime now reaches the adopted writer; the endpoint read compares local samples; command observation compares two full vectors; runtime metadata/root work is guarded, with typed profile and vector revalidation before the attempt recorder. The nine new cases cover disposal callbacks, getter/root/profile drift, unknown cleanup, outer disposal and identical physical Transform aliases.

**Open atomicity limit, not a blocker for the restricted command primitive.** Final admission calls `VerifyWriterProfile()`, observes the full vector, then calls `VerifyWriterProfile()` again. A callback during that last native profile inspection can mutate parent/local after the vector check; only managed lifetime checks remain before `InvokeRaw`. Likewise, the final getter of the second vector pass can mutate an earlier endpoint after both passes sampled it. A setter for another endpoint may then enter while the full vector has a third state. The outer source comparison excludes mutable local poses, so its postcheck cannot identify this predispatch condition.

The command's `Before` must be described as observed prestate; `ExternalEffectsUnclosed` and `SideEffectsUnknown` remain true. A future atomic or complete Seek claim requires a callback-free engine/native gate or versioned observation. Repeating callback-capable reads cannot make the vector atomic. Targeted tests could characterize these windows, but cannot establish atomicity.

No remaining blocker was found in the reviewed repairs for a parent-equal local-position command primitive with that contract. The 17-case controller was live during this static review, and its result was not used. The candidate remains isolated; it does not enable SetParent, world/camera/scene targets, ring/renderer effects, full Seek or complete SDK admission.
