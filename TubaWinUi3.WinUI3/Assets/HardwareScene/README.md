# Hardware scene

`scene.js` authors the laptop and desktop meshes, opening/boot animation, fan rotation,
OrbitControls interaction and projected component anchors. Component GLBs and their attribution are listed in models/README.md;
Three.js 0.186.1 is MIT licensed (see THREE-LICENSE.txt). All component assets load from the local package, only when a card is expanded.
The checked-in `scene.bundle.js` lets normal .NET builds run without Node.js.

To rebuild the bundle, install dependencies in a directory **outside Assets** (so they
are not copied into app output):

```powershell
npm install --prefix "$env:TEMP/tuba-hardware-scene-build" three@0.186.1 esbuild@0.28.2
node TubaWinUi3.WinUI3/Assets/HardwareScene/build.mjs "$env:TEMP/tuba-hardware-scene-build"
```

The native page constructs the renderer only for diagram mode. Classic mode never
creates a WebView2. Switching back disposes the control. Only explicit screenshot
sharing captures a PNG; interaction renders directly with WebGL.

Quick device check reuses this scene with `inspection: true`. Its `inspectionFocus`
message (`label`, or `null` for the panorama) moves only the camera and its target
over the same laptop/desktop. It never loads component GLBs or extracts parts.
Retargeting starts from the current camera pose, including during a transition;
dragging cancels that transition. During stress tests, the original screen gets a
dynamic canvas texture with the existing runner's telemetry. Warm light and rising
heat ribbons use at most 24 scene frames/second (4 with reduced motion); the screen
texture updates at 4 Hz. Stopping restores the screen texture and studio lighting.
Desktop fans ease from 4 to 28 radians/second while stress is active, with faint
trailing blade poses to keep the speed change visible at 24 fps; stopping eases
back to idle. Reduced motion disables fan rotation and rising heat ribbons.

The keyboard step expands the native viewport while pulling the camera back
into the shared `KeyboardTest/scene-controller.js` scene on the same WebGL canvas.
The pullback takes 1.1 seconds and starts alongside the 450ms viewport expansion.
The original computer keeps its studio materials and fades independently over a
gentler 1.4-second timeline, without delaying keyboard arrival. The keyboard scene omits its
large colored floor, so the native page background remains visible throughout.
The screen step prewarms keyboard shaders, textures, buffers and shadows offscreen.
Native resize animation holds one drawing buffer, and static keyboard shadows are
only updated when key travel changes. Caps are instanced and legends are batched.
The native page owns one physical-input hook and creates the original 2D control
only after scene/keyboard initialization fails (including timeout or context loss).
Laptop legends use per-cap coordinates and atlas UVs instead of a stretched overlay.
Both hardware and inspection pages read the current local Windows wallpaper through
`DesktopWallpaperService`, preserving its aspect ratio with a 2048px size cap; an
unreadable image falls back to the generated screen background.

Finishing inspection retains the WebView, computer and current stress report.
The native completion layout puts the scene beneath the summary; `inspectionComplete`
restores full computer opacity and smoothly frames the panorama for its new size.
`completion.js` draws one 3.8-second confetti burst (88 ribbons) and a subtle settling
ring. It then drops all particles and sleeps, leaving a static contact glow.
Reduced motion skips the ribbons and ripple. Returning to review keeps the same
stress control/report; only an explicit new inspection resets that session.
