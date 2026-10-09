Icons are downloaded unchanged from their owners:

- `github.svg`: [GitHub Octicons mark-github-16](https://github.com/primer/octicons/blob/main/icons/mark-github-16.svg),
  MIT license in `octicons-LICENSE.txt`.
- `discord.svg`: [Discord's official brand assets](https://discord.com/branding), black symbol. Discord owns the logo;
  its brand guidelines apply.
- `settings.svg`: [GitHub Octicons gear-16](https://github.com/primer/octicons/blob/main/icons/gear-16.svg), MIT license
  in `octicons-LICENSE.txt`.
- `minimize.svg`: [GitHub Octicons dash-16](https://github.com/primer/octicons/blob/main/icons/dash-16.svg), MIT license
  in `octicons-LICENSE.txt`.

These use the editor's SVG renderer to preserve curved paths, cutouts, and the original viewBox proportions.

`prowl.svg` is the engine's existing `EditorIcons.ProwlLogo` path from `Prowl.Editor/Theming/EditorIcons.cs`, with its
original 282 × 264 viewBox (Prowl's MIT license). It uses the shared `EditorSvgIcon` renderer.
