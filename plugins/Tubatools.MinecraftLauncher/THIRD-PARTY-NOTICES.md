# Third-party notices

This plugin uses the following third-party components:

## Six Labors ImageSharp 3.1.12

- Copyright (c) Six Labors
- License: Six Labors Split License 1.0 (Apache License 2.0 for use in open-source projects)
- https://github.com/SixLabors/ImageSharp

ImageSharp is used only to decode Modrinth WebP icons into PNG before displaying them in WinUI.

## System.Security.Cryptography.ProtectedData 10.0.0

- Copyright (c) .NET Foundation and Contributors
- License: MIT
- Used for Windows DPAPI encryption of Microsoft access/refresh tokens.

## Microsoft OAuth public client ID

- Default public client ID `c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb` is published by
  PrismLauncher (GPL-3.0) in its source; it is a public identifier, not a secret, and it is
  approved for Minecraft Services login.
- `e04809bd-5675-406f-850d-e69b7ae0387e` is a project-provided Azure app ID kept as an optional
  `UserClientId`; using it for Minecraft requires Xbox Developer program / Minecraft API approval.
