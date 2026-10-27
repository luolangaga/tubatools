Requirements:
-Windows 7 SP1 up to Windows 11, x64 and ARM64 (Windows on Arm runs DDU through the built-in x64 emulation)
-NVIDIA, AMD, Intel, Lisuan or Qualcomm (Snapdragon X) GPUs
-Realtek or Creative audio (optional)
-Microsoft .NET Framework 4.8 or higher

Recommended usage:
-You MUST disconnect your internet or completely block Windows Update when running DDU until you have re-installed your new drivers.
-DDU should be used when having a problem uninstalling / installing a driver or when switching GPU brand.
-DDU should not be used every time you install a new driver unless you know what you are doing.
-The tool can be used in Normal mode but for absolute stability when using DDU, Safemode is always the best.
-If you are using DDU in normal mode, Clean, reboot, clean again, reboot.
-Make a backup or a system restore (but it should normally be pretty safe).
-It is best to exclude the DDU folder completely from any security software to avoid issues.
-If you encounter issues, read the "Issues and solutions.txt"

Laptops (Intel and Qualcomm):
-Your PC maker ships part of the graphics driver as separate "extension" packages: panel and power settings on Intel, files the GPU needs to start on Qualcomm (Code 31 without them).
-The generic driver from Intel or Qualcomm does not include them. Only Windows Update or your PC maker's driver package can bring them back.
-DDU keeps these extensions by default. Removing them is an "Expert options" choice in the Intel and Qualcomm tabs: only tick it if you can reinstall your PC maker's package afterwards, or run: pnputil /add-driver <extension .inf> /install
-The camera driver is kept as well when the camera sits under the GPU; it comes back with the graphics driver.

For a guide , check : https://www.wagnardsoft.com/content/ddu-guide-tutorial....

Trademarks:
NVIDIA, AMD, Intel, Realtek, Creative, Lisuan, Qualcomm and Snapdragon, and their logos, are trademarks of their respective owners. They are used here only to identify the hardware DDU supports. Wagnardsoft is not affiliated with or endorsed by any of them. The MIT license covers the DDU code, not these names or logos.
