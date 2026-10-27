# Homepage hero artwork

`hero-light.png` is the layout approved by the user on 2026-10-27. `hero-dark.png`
uses the same arrangement with charcoal materials. These are generated image assets,
not the original reference images and not WinUI Gallery's banner.

The user supplied the two `WW_win95_v004_*_wide_post.png` pictures as art direction.
The approved illustration keeps their tactile beveled desktop style, with a Tuba
Toolbox window, quick inspection, disk treemap, game monitor, purple wrench-folder
branding, and small CPU-Z / AIDA64 details. Upper-left space is reserved for live
XAML text; no app greeting/title is rasterized into the banner.

Generated with the built-in image generation tool. Prompt summary: retain the
approved light composition exactly; change only palette/materials/contrast to
charcoal and desaturated teal, following the supplied dark reference. Preserve
window positions, quiet upper-left area and all small Tuba details.

Theme dictionaries choose the image automatically. High contrast hides the
illustration. The header fits the complete image without cropping, extends its
quiet background to the left, and fades its lower edge into the page. Only the
header displays artwork; the rest of the page remains transparent to the
window's configured Mica backdrop. Featured cards use a single horizontal
scrolling row below the illustration.
