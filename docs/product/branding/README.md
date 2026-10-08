# Lajur visual identity

The logo combines an uppercase **L** with two ledger lines. It represents quick,
orderly transaction capture into the user's workbook. The symbol has no small
text, currency sign, or chart detail, so it remains readable as a favicon.

## Source and deliverables

`lajur-mark.svg` is the editable source for the symbol. The built-in imagegen tool
created the initial concept (`lajur-concept.png`); the production paths were then
redrawn as clean vector geometry. Production applications load local assets only.
There is no image-generation service or runtime dependency in either application.

| Asset | Use |
| --- | --- |
| `lajur-mark.svg` / `.png` | Transparent standalone symbol; white/mint, use on a dark background |
| `lajur-logo.svg` / `.png` | Symbol and Lajur wordmark, for light backgrounds |
| `lajur-logo-dark.svg` / `.png` | Wordmark for dark backgrounds |
| `lajur-icon.svg` | Square icon, scalable without loss of detail |
| `lajur-icon-512.png` / `lajur-icon-1024.png` | Store/artwork exports with a full square background |
| Android `drawable/ic_launcher_*` | Adaptive foreground/background; the existing monochrome mapping uses the foreground alpha |
| Android `mipmap-*` | Square/round lossless WebP at mdpi through xxxhdpi |
| Web `wwwroot/brand/` | SVG, 16/32 px favicons, 180 px Apple touch icon, 192/512 px icons |
| Web `wwwroot/favicon.ico` | Multi-size 16/32/48 px desktop fallback |

Palette: teal `#0F6E63`, white `#FFFFFF`, mint `#B9F2DF`. The web already uses this
teal accent. Application interface theme preferences remain independent of the
fixed icon palette. Logo wordmarks use a system sans-serif font; live application
text follows the platform font and theme, including dark mode.

The adaptive foreground is drawn on a 108 dp canvas with generous margins and
fits the centered 66 dp safe circle. Do not pre-round the full-background store
icon or the adaptive background; the launcher applies its own mask. A decorative
image adjacent to visible "Lajur" text has an empty/null accessibility description
to avoid announcing the name twice.

## Regenerate exports

Development tools only: Node.js and `sharp`. From the repository root:

```powershell
npm install --prefix private/output/branding-tooling --no-save --no-package-lock sharp
node docs/product/branding/export-icons.cjs (Resolve-Path private/output/branding-tooling/node_modules/sharp).Path
```

The optional module argument must be an **absolute path** if it is outside Node's
normal resolution path.

The script exports the Android vectors, all raster densities, SVG/PNG logos, and
web icon files from the same source paths. It intentionally replaces these
generated exports. Existing adaptive-icon XML files already reference these
drawables and supply the monochrome layer.

## Original imagegen prompt

```text
Use case: logo-brand
Asset type: master transparent symbol for Lajur Android adaptive launcher icon, web favicon, and app identity.
Primary request: create a distinctive, beautifully balanced minimal geometric logo for Lajur, an Indonesian personal finance entry app connected to a personal spreadsheet. A sturdy uppercase L formed from a vertical white stroke and bottom white horizontal stroke, with two shorter pale mint horizontal ledger strokes floating in its open upper-right interior. The resulting silhouette combines an unmistakable L and an orderly financial ledger.
Style: impeccably clean flat vector-like raster, exact crisp edges, softly rounded stroke corners, bold simple geometry, no texture, no gradients.
Composition: one standalone compact symbol, square canvas 1024 by 1024; symbol centered, occupies approximately the central 62 percent of width and height, generous even transparent margins. White L and pale mint #B9F2DF ledger strokes. Keep few elements and ample separation for recognition at 16px.
Backdrop: genuine transparent alpha background; this white/mint symbol will be placed on solid deep teal #0F6E63 by the project.
Constraints: no text, no wordmark, no shadow, no border, no app tile background, no mockup, no chart arrow, no currency signs, no coin, no cloud, no watermark. Deliver a single finished mark only.
```

## Visual smoke checks

- Android: install the built APK, check the launcher mask and open Login. The logo
  precedes the existing tagline and inputs. Unlock also displays the same identity.
- Android themed icons: enable themed icons in a launcher that supports them and
  check that the monochrome L and ledger strokes remain separated.
- Web: open `/masuk`; check the logo, then `/buku` after signing in. Both use the
  same SVG. Check the tab favicon and add-to-home-screen icon.
- Inspect the 16 px favicon and square/round icon exports for legibility.

No user financial data is needed for branding verification.

Verification on 2026-10-08: Android build, unit tests and lint passed; all 9 web
viewer integration tests passed. The browser login displayed its loaded SVG and
versioned favicon links. The debug APK installed/launched on a disposable Android
37.2 AVD and its UI hierarchy confirmed the logo/name and login layout. Android
screenshots remain protected by the existing `FLAG_SECURE` policy. Physical-device
launcher masks/themed icons and authenticated Unlock still need visual review.
