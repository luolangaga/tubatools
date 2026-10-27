`Controls/DashboardGalleryTile.xaml(.cs)` is ported directly from WinUI Gallery's
`WinUIGallery/Controls/HomePage/Tile.xaml(.cs)`. It retains Gallery's acrylic surface,
stroke resources, spacing and typography. URL navigation is replaced with the toolbox's
tool commands. The homepage keeps Gallery's 232 × 172 DIP default size, with a compact
layout on small windows. Content can be edited; there is no position or size editor.
`Styles/DashboardGallerySelectorBar.xaml` is copied from Gallery's `Styles/SelectorBar.xaml`.
Copyright (c) Microsoft Corporation. MIT license: GALLERY-LICENSE.txt.

The WinUI Gallery banner image is intentionally not included. The homepage uses native theme surfaces.

The six SVG tool illustrations in this directory are original dashboard assets. External tools retain their own executable / shell logos.
