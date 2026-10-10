# Prowl v1.0-preview-4 is out!

## Prefabs, rebuilt

The biggest chunk of this release. Rebuilds are now in-place, so per-instance state and your selection survive a refresh. Overrides are keyed by stable source identifiers instead of by index, so reordering components no longer scrambles them. Models are prefabs now, assets can inherit other assets, instances refresh on import, and a long list of guards makes it genuinely hard to corrupt one. Prefab nesting has been removed.

## Audio overhaul

IAudioEffect becomes a serializable AudioEffect base, so effect chains are authorable and serialize properly. New AudioMixerGroup with a real mixer editor, import settings (load type, force mono, sample rate), waveform previews and clip audition. One-shots play on pooled voices instead of stealing the source, clips store 16-bit PCM, devices track by name, and Delay, Reverb, Filter, Phaser and Distortion all had real bugs fixed.

## Physics

Query API overhauled with LineCast, RaycastAll and a new QueryFilter. Added RigidbodyConstraints, rigidbody interpolation and ForceMode. CollisionMatrix is copy-on-write and thread-safe, and FixedUpdate now runs before the step. Terrain finally behaves: rotation, scale and holes are all respected in collisions and queries. Plus gizmos for every constraint type.

## Orthographic rendering

Now actually works. TAA, Skybox, SSR, GTAO, Depth of Field and Volumetric Fog all handle ortho correctly, depth linearization is fixed for both projections, and the Scene View has a proper toggle with its own controls.

## Shaders & Models

The Standard shader was completely refactored, with Cutout, Transparent and Double Sided variants across Standard, Standard Anisotropic and Unlit, plus a new popup shader selector. Models import with multiple UV channels per material matching the GLTF spec, use Clay's normals and tangents, and bring in lights and cameras.

## Image loading, native free

Loading and saving moved to Prowl.Aperture, our own dependency-free decoder, and Magick.NET is gone along with its native binaries. Fourteen formats instead of eleven, so GIF, WebP, ICO, Netpbm and camera RAW now import, and it is faster than what it replaced.

## Editor QoL

Playmode can be intercepted, and the inspector defers it when you have unapplied changes. Entering or exiting playmode unloads all assets for fresh instances. Every asset editor moved to one unified Apply/Revert structure. New Enable If and Inspector Name attributes, Debug.LogOnce, a stack of new debug draw primitives, and a new Origami View Manipulator widget.

## Packages

Prowl packages bumped from v3.0.1 to v3.3.0.

Plus Terrain Details remade, Windzones for terrain and particles, and a mountain of fixes across sprites, textures, colliders, the asset pipeline and the console.
