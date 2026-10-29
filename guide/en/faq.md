[← Back to README](../../README_EN.md)

# FAQ

**Q: Why does it trigger UAC on launch?**
Burn-in tests, the FPS overlay, junk cleaning and time sync all need to read sensors or write system settings, so they require elevation. The unpackaged build requests administrator rights once at startup; the Microsoft Store build runs under package identity and does not force elevation.

**Q: Are the bundled third-party tools official?**
The 89 third-party tools are copyrighted by their respective authors and are bundled for learning and evaluation only — please support the originals. The 47 built-in tools are native to this project, and their source code is right here in this repository.

**Q: Can I carry it on a USB drive?**
Yes. The portable build runs straight after extraction, and settings default to `%LocalAppData%\TubaWinUi3\`. To keep your data next to the app instead (for example on an external drive), place a `.config_location` marker file in the program folder and settings move to `Data\` beneath it.

**Q: Antivirus flags it — what should I do?**
Some tools legitimately operate at system level (drivers, registry, cleanup), so heuristic engines may flag them. Official release artifacts are signed through SignPath; please download only from the [official channels](getting-started.md#installation).

**Q: Will it slow my PC down?**
There are no background services: hardware monitoring runs only while its page or an in-game overlay is open, and the overlay and co-op helper end when you close them — nothing lingers, nothing runs behind your back. The optional "close the window = minimize to the tray" switch (off by default) merely keeps the app resident so reopening it needs no new hardware scan; right-click the tray icon and choose Exit to end it completely, or flip the option under Settings → General at any time.

**Q: A tool won't start, or complains about missing DLLs?**
Use "Runtime Repair" to install the missing Visual C++ 2008-2026, .NET Framework and DirectX components in one click. Legacy document conversion needs a locally installed Office or WPS — if neither is present, you get a clear message instead of a silent failure.

**Q: Which models can the AI Assistant use?**
A built-in model works out of the box, and you can point it at any OpenAI-compatible endpoint (DeepSeek / OpenAI / a local model…). API keys are stored on your machine only.

**Q: Where are the logs?**
By default under `%LocalAppData%\TubaWinUi3\` (`Data\` next to the app in portable mode): `agent-debug.log` is the AI Assistant log, and startup crashes are written to `%TEMP%\app_crash.log`.
