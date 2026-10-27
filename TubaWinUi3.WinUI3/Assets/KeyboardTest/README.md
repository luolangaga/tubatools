# 3D keyboard test

Offline Three.js scene hosted by `KeyboardTest3DControl` using the shared WebView2 environment.
The original `KeyboardTestControl` remains the 2D fallback; only one input hook is active at a time.
`KeyboardTestInput` reads the complete KBDLLHOOKSTRUCT and confines interception to this process's foreground windows.

The scene uses simple voxel boxes and a locally drawn nearest-filtered wood texture: the keyboard rests on a wooden desk with block legs. There are no rounded metallic models, studio environment maps, or web HUD elements. Coverage, recent keys, camera status, and commands are native WinUI controls outside the WebView2 canvas.

Physical input alone marks keys as tested. Pointer selection previews the camera without marking a key.
Camera and key travel use analytic critically damped springs with continuous velocity across target changes.
The idle deadline starts after all keys are released; after 1000 ms, the camera smoothly returns to the half-overhead panorama.
Repeated keydown events do not inflate coverage. Static scenes stop rendering; hidden/unloaded scenes stop rendering and release input.

`layout.js` is the physical layout and spring source; `scene.js` is the standalone renderer and bridge.
`scene-controller.js` owns the reusable scene; QuickDeviceCheckPage shares it with
the computer model's renderer so its full-width transition never swaps WebViews.
Keycaps use one instanced draw and legends use one dynamic mesh. Their colors and
travel remain independent, including simultaneous input. Pressed caps use saturated
accent color with pale legends, and tested caps retain a stronger green tint.
Atlas anisotropic filtering and up to 2x native rendering keep slanted text readable.
The committed `scene.bundle.js` is the runtime file, with no CDN or network dependency.

Rebuild with Node.js and a separate tooling directory (do not change the repository's website dependencies):

```powershell
npm install --prefix "$env:TEMP\tuba-keyboard-build" three@0.180.0 esbuild@0.25.11
node TubaWinUi3.WinUI3/Assets/KeyboardTest/build.mjs "$env:TEMP\tuba-keyboard-build"
node --test TubaWinUi3.Tests/keyboard-scene.test.mjs
```

The WebGL integration test is `TubaWinUi3.Tests/keyboard-webgl.test.mjs`; run it with a tooling directory containing Playwright, a Chromium executable path, and an optional screenshot output directory (see its header).

Three.js 0.180.0: MIT, see `THREE-LICENSE.txt`. Keyboard geometry and labels are generated locally.
