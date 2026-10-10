# Prowl v1.0-preview-3 is out!

## Hot Reloading

Change a script, hit save, keep playing. Powered by our new Ember reload engine, component fields, object identity, and delegates into your methods all migrate onto the recompiled types in place. Both assemblies stay loaded, no domain unload, no scene reload. The editor's own references (selection, inspector target, hierarchy) come along for the ride. Its seamless, even during Play-Mode!

## Scene Tools, rebuilt

Marquee selection, pivot mode + orientation toggles (local/global, pivot/center), a proper SceneDrawList for 3D debug drawing, context-aware cursor shapes over gizmos, and a cleaner Control/Handle API for custom tools. Transform gizmos are 25px chunkier too.

## Build system overhaul

Fully rewritten: proper build staging, real async throughout, better asset packaging, and platform targets are now registered data instead of a hardcoded enum so new platforms (mobile, consoles, whatever) can plug in without touching the engine. Asset DB now validates + refreshes before every build so you can't ship a stale cache.

## Performance

SSR shader ~40% faster, GTAO cache usage significantly optimized, fewer per-frame allocations in the renderer, UI, and camera gathering and a memory leak in texture importing squashed.

The Editor itself is easily ~200% faster on average!

## GameObject UI overhaul

New Graphic base type, tons of GameObject UI refactors, more RectTransform utilities, working cross-canvas navigation, cursor wrapping support, and fixes to anchor/parent rect resolution, dropdowns, and layout caching.

## Scene & lifecycle

Scene API refactored and made idempotent, Scene.DontDestroyOnLoad(go), EngineObject.Destroy() now defers disposal to end-of-frame, MonoBehaviours moved onto SceneDispatcher for a solid perf win, with physics callbacks now proper virtual methods.

## Editor QoL

Copy / Paste as New / Paste Values on components, + button in the Hierarchy with Create Empty Child/Parent, Align With View in the context menu, a much better rename + create flow, auto SLNX generation on compile, Ctrl+S now saves dirty materials, empty-folder icons, error toasts when a project fails to open.

## Tooling

new Prowl.Analyzers project with a null-coalescing/conditional analyzer that catches ?. and ?? misuse on EngineObjects, nullable annotations across runtime and user code, Prowl packages bumped all the way to v3.0.1.

Plus a mountain of enhancements and fixes in the asset pipeline, physics, gizmo, colliders, individual assets, render targets, the list goes on.
