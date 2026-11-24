# Closed wrapper metadata — 2026-10-21

This directory records an actual Rizline session with the opt-in marker enabled. The game reached the normal song flow and emitted the `closed-local-wrapper-metadata` stage. The captured rows prove, from the running IL2CPP metadata, the exact closed `ResourceReference<UnityEngine.Sprite>` and `ResourceReference<UnityEngine.AudioClip>` classes. The AudioClip row also verifies assignability to the exact `IResourceReference<UnityEngine.AudioClip>` contract taken from `InsideLevelParams.musicReference`.

The capture is deliberately metadata-only. It allocates only `System.Type` and `System.Array` objects used by the reflection bridge; it does not construct a ResourceReference, create or replace a Unity asset, invoke a getter, invoke `Release`, or claim ownership. The existing live object observed elsewhere is `AssetRefReference<T>`, which remains a distinct concrete type.

Evidence: `actual-game-events.jsonl` is the raw event slice from the diagnostic session; `actual-game-proof.json` is the parsed proof with the eight wrapper rows and explicit boundaries.
