ZenTimings
A free, simple and lightweight app for monitoring memory timings on Ryzen platform.

# System Requirements
- AMD Ryzen, Threadripper or EPYC processor
- .NET Framework 4.5 or newer
- PawnIO (bundled with the app)
- InpOut (bundled with the app)
- Supported OS: Windows 10/11 (64bit)

# Installation
Extract the downloaded archive anywhere on the disk.
InpOut64 driver gets automatically installed on first launch.
Location of the installed driver is /System32/drivers/inpout64.sys.

To uninstall the driver you may use a manual method or a utility such as Autoruns for Windows.

# Functions
Common Timings
The main purpose of the app is to show all the impotant timings info on a single screen.
Currently it's in read-only mode, since adjusting timings on-the-fly is not possible on Ryzen, without a reboot.
Due to differences between CPU generations, BIOS versions and motherboards, some parameters might not be detected,
although the timings should be available on all platforms, including mobile APUs.

# Frequencies
Infinity Fabric and Memory Controller clocks can be detected on most of the desktop SKUs, however the values are not
aware of current base clock (BCLK). ZenTimings will make an attempt to correct them, but it will work in limited cases,
where configured DRAM frequency is reported correctly.
Result will vary between different BIOS implementations and AGESA versions.

# Voltages
The app tries to read several voltages related to the memory controller, but again, it all depends on the platform
and the information BIOS is reporting.

# Screenshot
A screenshot of the app window can be automatically saved or copied to clipboard with a click of a button.
Click on the "camera" button at the top right corner. The screenshot can be copied to the clipboard for direct paste
in supported apps or saved as a file to the file system.

# Auto Refresh
Auto Refresh is enabled by default and updates frequencies and voltages every 2 seconds.
The feature can be disabled from the Options dialog. The interval is user-configurable.

# Themes
Supports light and dark modes, which can be changed runtime.
Go to Options and select the desired theme. Save.

# Debug Mode
When certain parameters are read wrong or not displayed at all, a handy debug window provides an essential info which might help the developer.
From the Tools menu, select Debug Report and click on Debug. When ready, the report can be saved as a text file.
