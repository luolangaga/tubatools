
CPU-Z Readme file
------------------

Version 2.20.1
May 2026
Contact : cpuz@cpuid.com
Web page: https://www.cpuid.com/softwares/cpu-z.html
Validation page : https://valid.x86.fr/
Hall of Fame : https://valid.x86.fr/records.php
CPUID SDK : https://www.cpuid-pro.com/products-services.php


Configuration file (cpuz.ini)
------------------------------

The configuration file must be named cpuz.ini and be present at the same directory level
as cpuz.exe. It contains the following :

[CPU-Z]
VERSION=x.x.x.x
TextFontName=
TextFontSize=14
TextFontColor=000080
LabelFontName=
LabelFontSize=14
ACPI=1
PCI=1
MaxPCIBus=256
DMI=1
Sensor=1
SMBus=1
Display=1
UseDisplayAPI=1
BusClock=1
Chipset=1
SPD=1
XOC=0
CheckUpdates=1


- TextFontName : Font used for the information boxes. 
- TextFontSize : Size of the font used for the information boxes. 
- TextFontColor : Color of the font used for the information boxes. Value is expressed in hexadecimal, and consists in a classic Red/Green/Blue color code : RRGGBB 
- LabelFontName : Font used for the label boxes. 
- LabelFontSize : Size of the font used for the label boxes. 
- Sensor : Set to OFF (or 0) disables sensor chip detection and voltages measurement. 1 to enable.
- PCI : Set to OFF (or 0) disables the PCI information. This disables chipset, SPD and, depending on the hardware, sensoring information. 1 to enable.
- MaxPCIBus : Sets the maximum PCI bus to scan. Default value is 256.
- DMI : Set to OFF (or 0) disables the DMI (Desktop Management Interface) information. This concerns BIOS vendor and version, motherboard vendor and revision. 1 to enable. 
- SMBus : Set to OFF (or 0) disables SMBus information : SPD, and, depending on the hardware, sensoring information. 1 to enable.
- Display : Set to OFF (or 0) disables the video card information reported in the validator. 1 to enable.
- UseDisplayAPI : Set to 1, uses the display driver to read the display adapters information.
- BusClock : Set to 1, uses the bus clock as primary clock source. Set to 0 to use an alternate method.
- Chipset : set to OFF (or 0) disables the memory controller and southbridge information. 1 to enable.
- SPD : set to 0 to disable SPD reading. 1 to enable.
- XOC : eXtreme Overclock Mode : runs CPU-Z with as little system load as possible. Your system won't be fully validated before it is manually checked. Requires to restart CPU-Z.
- CheckUpdates : set to 0 to disable the new version checker at startup. 1 to enable.

Parameters
----------

-txt=filename : Launch CPU-Z in ghost mode (no interface appears) and generates the register dump file (.txt) 
in the same directory as the exe file.

-html=filename : Same as "-txt" except it generates the html report.

-core=id : Displays clock speed of core #id (id can be set from 0 to Number of cores minus one).

-bench : runs CPU-Z without interface, runs the benchmark, and saves the result in a TXT file named like the machine.


Keys
----

F5 : save the current tab in a bmp file
F6 : save the current tab in the clipboard
F7 : save cvf file in the current directory


History
-------
--------------------------------------------------------------------------------------------------
2.20.1 - May 2026
- AMD Ryzen 7 7700X3D (Raphael)(2.20.1).
- AMD Ryzen AI Max+ 495, 492, 488 (Gorgon Halo).
- AMD Ryzen AI Max 490, 485 (Gorgon Halo).
- AMD Ryzen AI Max PRO 495, 490, 485, 480 (Gorgon Halo).
- AMD Ryzen 9 9950X3D2 (Granite Ridge).
- AMD Ryzen 9 PRO 9965X3D, PRO 9945 (Granite Ridge).
- AMD Ryzen 7 PRO 9755, PRO 9745 (Granite Ridge).
- AMD Ryzen 5 PRO 9645 (Granite Ridge).
- AMD Ryzen AI 7/PRO 450G/GE (Gorgon Point 2).
- AMD Ryzen AI 5/PRO 440G/GE (Gorgon Point 2).
- AMD Ryzen AI 5/PRO 435G/GE (Gorgon Point 3).
- AMD Ryzen AI Max+ 392 (Strix Halo).
- Intel Core Ultra 5 250KF Plus (Arrow Lake Refresh).
- Intel Core 7 360 and 350 (Wildcat Lake).
- Intel Core 5 330, 320 and 315 (Wildcat Lake).
- Intel Core 3 304 (Wildcat Lake).
- Intel Core 9 273PQE, 273PTE, 273PE (Bartlett Lake).
- Intel Core 7 253PQE, 253PTE, 253PE, 251TE, 251E (Bartlett Lake).
- Intel Core 5 223PQE, 223PTE, 223PE, 221TE, 221E, 213PTE, 213PE, 211TE, 211E (Bartlett Lake).
- Intel Core 3 201TE, 201E (Bartlett Lake).
- Intel Arc Pro B70 and B65 (BMG-G31).
- Intel Arc Pro B60 and B50 (BMG-G21).
- Support of HUDIMM and HSODIMM memory modules.

--------------------------------------------------------------------------------------------------
2.19 - March 2026
- AMD Ryzen AI 7/PRO 450G/E, AI 5/PRO 440G/E & 435G/E (Kraken Point 2).
- AMD Ryzen AI 9 HX 470.
- Fix AMD Ryzen 5 5500U (Lucienne) reported as 7350U (Cezanne).
- Preliminary support of Intel Wildcat Lake.
- CQDIMM (4-ranks CUDIMM) memory support.
- Fix DLL hijacking vulnerability thanks to Kwangyun Kem.
- New Chinese translation thanks to Shinjo Kurumi.

--------------------------------------------------------------------------------------------------
2.18 - January 2026
- AMD Ryzen 7 9850X3D (Granite Ridge).
- AMD Ryzen 5 7500X3D (Raphael).
- Preliminary support of AMD Medusa Point.
- Preliminary support of Intel Arrow Lake Refresh.
- Improved support of LPDDR5X CAMM2.

--------------------------------------------------------------------------------------------------
2.17 - October 2025
- Intel Core Ultra X9 388H, Core Ultra X7 368H and 358H, Core Ultra X5 338H (Panther Lake-H).
- Intel Core Ultra 9 375H, Core Ultra 7 355H and 345H, Core Ultra 5 325H (Panther Lake-H).
- Intel Core Ultra 7 360U, Core Ultra 5 350U and 340U, Core Ultra 3 320U (Panther Lake-U).
- Intel Core Ultra 3 205 (Arrow Lake).
- Intel Core 3/5/7 2xxE (Bartlett Lake).
- Intel Core i5 110 (Comet Lake).
- AMD Ryzen 5 5600F (Vermeer).
- AMD Ryzen 9 PRO 9945, Ryzen 7 PRO 9745, Ryzen 5 PRO 9645 (Granite Ridge).

--------------------------------------------------------------------------------------------------
2.16 - July 2025
- AMD Ryzen Z2 and Z2 Extreme (Strix Point).
- AMD Ryzen 9 8945HX, 8940HX, Ryzen 7 8840HX, 8745HX (Dragon Range refresh).
- Improved support of AMD Ryzen AI 7 350 and Ryzen AI 5 340 (Kraken Point).
- Zhaoxin KaiXian KX-U6780A and KX-U6580 (LuJiaZui, 8 cores).
- AMD Radeon RX 9060 XT (Navi 44).
- NVIDIA RTX 5060 Ti (GB206), RTX 5060 (GB206), RTX 5050 (GB207).

--------------------------------------------------------------------------------------------------
2.15 - March 2025
- Improved support of Intel Arrow Lake-H processors.
- AMD Ryzen AI Max+ 395 & AI Max+ PRO 395, Ryzen AI Max 390 & AI Max 385 (Strix Halo).
- AMD Ryzen AI 7 350, Ryzen AI 5 340  (Kraken Point).
- AMD Radeon RX 9070 XT & 9070 (Navi 48).
- AMD Ryzen 7 5705G, 5705GE, Ryzen 5 5605G, 5605GE, Ryzen 3 5305G, 5305GE (Cezanne).
- AMD EPYC 9965 (192C/384T), 9845 (160C/320T), 9825 (144C, 288T), 9755 (128C/256T), 9745 (128C/256T),
9655 (96C/192T), 9645 (96C/192T), 9565 (72C/144T), 9555 (64C/128T), 9535 (64C/128T), 9455 (48C, 96T),
9355 (32C/64T), 9335 (32C/64T), 9135 (16C/32T), 9115 (16C/32T), 9015 (8C/16T).
- NVIDIA RTX 5070 Ti (GB203) & 5070 (GB205).
- Added GPU compute units (Graphics tab).
- New Validation Statistics page (https://valid.x86.fr/statistics.html).

--------------------------------------------------------------------------------------------------
2.14 - February 2025
- NVIDIA RTX 5090 & 5080 GPUs.
- AMD Ryzen 9 9955HX3D, 9955HX, 9950HX3D, 9950HX, 9850HX, 9845HX (Fire Range).
- Fixed benchmark graphs scaling.

--------------------------------------------------------------------------------------------------
2.13 - December 2024
- Intel Arc B580 GPU.
- Intel Arrow Lake-U preliminary support.
- Improved support of Intel Lunar Lake.
- Intel Q870, B860, H810, W880, HM870, WM890, WM880 chipsets.
- AMD X870/B840 chipsets.
- CAMM2 memory modules type.

--------------------------------------------------------------------------------------------------
2.12 - November 2024
- AMD Ryzen 7 9800X3D (Granite Ridge).
- Intel Core Ultra 9 285HX, Ultra 7 275HX/265HX/255HX, Ultra 5 245HX/235HX (Arrow Lake-HX).
- Intel Core Ultra 9 285H, Ultra 7 265H/255H, Ultra 5 235H/225H (Arrow Lake-H).
- Intel Core 7 160HL, 150HL, 160UL, 150UL, 150U (Raptor Lake).
- Intel Core 5 130HL, 120HL, 130UL, 120U (Raptor Lake).
- Intel Core 3 100HL, 100UL, 100U (Raptor Lake).
- CUDIMM DDR5 memory.

--------------------------------------------------------------------------------------------------
2.11.2 - October 2024
- Fixes ARL memory clock and timings (2.11.2).
- Adds microcode revision report in Mainboard tab / BIOS.
- Improved support of AMD Strix Point (including GPU).
- AMD Granite Ridge/Strix Point memory p-states.
- Improved support of Intel Arrow Lake clock multiplier granularity.
- Intel Core Ultra 5 235, 225 & 225F (Arrow Lake).
- Intel Core i9 14901KE & 14901E, Core i7 14701KE, Core i5 14501E & 14401E/F (Raptor Lake).

--------------------------------------------------------------------------------------------------
2.10 - July 2024
- AMD Ryzen 9 9950X (16C/32T), 9900X (12C/24T), Ryzen 7 9700X (8C/16T) and Ryzen 5 9600X (6C/12T) Granite Ridge (Zen 5).
- AMD Ryzen AI 9 HX 375 (4x Zen 5 + 8x Zen 5c), Ryzen AI 9 365 (4x Zen 5 + 6x Zen 5c) Strix Point APUs.
- AMD Ryzen 9 8945H, Ryzen 7 8845HS (Hawk Point).
- Intel Core Ultra 9 285K & 275, Core Ultra 7 265K & 255, Core Ultra 5 245K & 240 (Arrow Lake). 
- Intel Core Ultra 9 288V ; Ultra 7 268V, 266V, 258V, 256V ; Ultra 5 236V, 228V, 2266V (Lunar Lake).
- AMD Radeon RX 7600 XT (Navi 33 XT).

--------------------------------------------------------------------------------------------------
2.09 - January 2024
- Improved support of Intel Meteor Lake and preliminary support of Arrow Lake.
- AMD Hawk Point and Hawk Point 2 (Zen 4/Zen 4c).
- NVIDIA RTX 4070 SUPER (AD104-350).
- The benchmark can now be ran on a single coreset/cluster.

--------------------------------------------------------------------------------------------------
2.08 - September 2023
- Improved support of Intel Meteor Lake.
- Intel Core i5 14400F, 14400T, 14400, 14500 and 14500T.
- Intel Core i3 14100F, 14100T and 14100.
- Intel 300 and 300T processors (RPL, 2c, 65/35W).
- AMD Threadripper PRO 7995WX (STP, 96c), 7985WX (STP, 64c), 7975WX (STP, 32c), 7955WX (STP, 16c) and 7945WX (STP).
- Preliminary support for AMD EPYC Bergamo.
- Fixed delay generated by version checking. 

--------------------------------------------------------------------------------------------------
2.07.1 - September 2023
- Intel Core i9-14900K/KF (24c), Core i7-14700K/KF (20c),  Core i5-14600K/KF (14c).
- Fix memory information on Intel Core i5 13400F.
- Support of Intel Core 3, 5, 7 and Core Ultra 5, 7, 9 new branding.
- Preliminary support for Intel Meteor Lake.
- AMD Radeon RX 7800 XT and 7700 XT (Navi 32).

--------------------------------------------------------------------------------------------------
2.06.1 - June 2023
- AMD Ryzen Z1 and Z1 Extreme (2.06.1).
- AMD Ryzen 9 7940H & HS, Ryzen 7 7840HS (2.06.1).
- Fixed AMD EPYC Milan reported as Genoa (2.06.1).
- Intel Core i9 13980HX, 13900HX, 13900HK (2.06.1).
- Intel Core i7 13700H, 13650HX (2.06.1).
- Intel Core i5 13500HX, 13420H (2.06.1).
- Intel N97, N95 and N50 (ADL-N, 6W to 15W).
- AMD Dragon Range Ryzen 9 7945HX (16c/55-75W), 7845HX (12c/45-75W), Ryzen 7 7745HX (8c/45-75W), 7645HX (6c/45-75W).
- Zhaoxin C-1080 IGP (KX-6000G).
- Zhaoxin KH-40000 YongFeng (12/16/32c).
- NVIDIA RTX 4070 (AD104-250).
- NVIDIA RTX 4060 Ti (AD106-350).
- AMD Radeon RX 7600 (Navi 33 XL).

--------------------------------------------------------------------------------------------------
2.05.1 - March 2023
- Fix RTX 4090/4080/4070 detection bug (2.05.1).
- Fix DMI crash bug (2.05.1).
- Intel Xeon Sapphire Rapids platform.
- Preliminary support for AMD Storm Peak platform.
- Zhaoxin KX-6000G/4 CPU.

--------------------------------------------------------------------------------------------------
2.04 - January 2023
- Intel N100 and N200 (ADL-N).
- intel Core i3 N300 and N305 (ADL-N).
- Intel Core i5 13450HX, 13500HX, Core i7 13650HX, 13700HX (55W).
- Intel Core i9 13900KS.
- AMD Ryzen 9 7950X3D, 7900X3D, Ryzen 7 7800X3D.
- AMD Ryzen 9 7900, Ryzen 7 7700, Ryzen 5 7600 (RPL, 65W).
- AMD Athlon Gold 7220U, Ryzen 3 7320U, Ryzen 5 7520U (MDN-A0, 15W).
- AMD Radeon RX 7900XT/XTX.
- NVIDIA RTX 4080 16GB (AD103).
- NVIDIA RTX 4070 Ti (AD104).

--------------------------------------------------------------------------------------------------
2.03.1 - November 2022
- Intel Core i9 13900HK, Core i7 13700H/HX, 13650HX, 13620H, Core i5 13500H/HX, 13450HX, 13420H (2.03.1).
- Intel Celeron 7305 (2.03.1).
- Intel Core i9-13900/K/F/KF, Core i7-13700/K/F/KF, Core i5-13600/K/F/KF, Core i5-13500, Core i5-13400 and Core i3-13100.
- Intel ARC A770, A750, A580 (ACM-G10) and A380 (ACM-G11) GPUs.
- Support for multipliers > 64x on AMD Ryzen 7000.
- Preliminary support for NVIDIA RTX 4000 GPUs.

--------------------------------------------------------------------------------------------------
2.02 - August 2022
- AMD Ryzen 9 7950X, Ryzen 9 7900X, Ryzen 7 7700X, Ryzen 5 7600X.
- AMD X670E/B650 chipsets.
- AMD Radeon RX 6950 XT (Navi 21 KXTX), RX 6750 XT (Navi 22 KXT), RX 6650 XT (Navi 23 KXT) and RX 6400 (Navi 24 XL).
- Preliminary support of AMD EXPO memory overclocking profiles.
- Intel Z790 platform.
- Intel Xeon Platinum, Gold and Silver "Ice Lake-SP" (10nm, FCLGA4189).

--------------------------------------------------------------------------------------------------
2.01 - April 2022
- Glenfly Arise-GT10C0 GPU.
- Intel Core i9-12900T, Core i5-12600T (35W).
- Intel Atom x6427FE, x6425RE, x6425E, x6414RE, x6413E, x6212RE, x6211E, x6200FE (Elkhart Lake, FCBGA1493).
- Intel Pentium J6425, N6415 (Elkhart Lake, FCBGA1493).
- Intel Celeron J6413, N6211 (Elkhart Lake, FCBGA1493).
- Preliminary support for Intel ARC 3/5/7 (DG2).
- Preliminary support for Intel Raptor Lake (13th gen).
- AMD Ryzen 7 5700X, Ryzen 5 5600/5500.
- AMD Ryzen 3 5300GE, Ryzen 3 PRO 5350GE, Ryzen 5 PRO 5650GE, Ryzen 7 PRO 5750GE (Cezanne).
- AMD Ryzen 9 6980HX, 6900HX, Ryzen 7 6800H, Ryzen 5 6600H (45W).
- AMD Ryzen 9 6980HS, 6900HS, Ryzen 7 6800HS, Ryzen 5 6600HS (35W).
- AMD Ryzen 7 6800U, Ryzen 5 6600U (15-28W).
- AMD Ryzen 7 5825U, Ryzen 5 5625U, Ryzen 3 5425U (15W).
- AMD Rembrandt & Raphael APUs (RDNA2).
- AMD Mendocino APU (Zen2 + RDNA2).
- AMD Radeon RX 6850M XT GPU (NAVI22).
- AMD RX 6800S, RX 6700S, RX 6650M, RX 6650M XT GPUs (NAVI23).
- NVIDIA GeForce RTX 3090 Ti GPU (GA102-350, 450W).
- Improved information accuracy when core isolation is enabled.
- Improved validation process for high clock submissions (>6GHz).

--------------------------------------------------------------------------------------------------
2.00 - March 2022
- Intel Core i9 12900KS.
- Intel Core i7-1280P/1270P/1260P, Core i5-1250P/1240P, Core i3-1220P (28W).
- Intel Core i7-1265U/1255U, Core i5 1245U/1235U, Core i3 1215U (15W).
- Intel Core i7-1260U/1250U, Core i5 1240U/1230U, Core i3 1210U (9W).
- Intel Pentium 8505, Celeron 7305 (15W).
- Intel Pentium 8500, Celeron 7300 (9W).
- AMD Ryzen 7 5800X3D.
- AMD Radeon RX 6500 XT.
- Preliminary support of AMD Ryzen 6000 "Rembrandt" APUs.

--------------------------------------------------------------------------------------------------
1.99 - January 2022
- Intel Core i5 12500, 12400, Core i3 12300, 12100 CPUs.
- Improved DDR5 SPD information report.
- AMD CCD/CCX topology bug fix.

--------------------------------------------------------------------------------------------------
1.98.2 - November 2021
- Intel Core i7 12700H (1.98.2)
- Improved validation process for Alder Lake records (1.98.2)
- Improved support for AMD Cezanne 5600G/5700G APUs (1.98.2)
- AMX instruction set support.
- AMD 4700S processor.

--------------------------------------------------------------------------------------------------
1.97 - August 2021
- Intel Core i9 12900K, Core i7 12700K and Core i5 12600K "Alder Lake" CPUs and Intel Z6xx platform.
- DDR5 memory SPD and XMP 3.0 support.
- AMD Radeon 6600XT GPU.

--------------------------------------------------------------------------------------------------
1.96.1 - May 2021
- Intel Iris Xe GPU (DG1) (1.96.1).
- Improved support of Centaur CHA processor (1.96.1).
- Preliminary support of Intel Alder Lake and Z6xx platform.
- Preliminary support of DDR5 memory.
- AMD Ryzen 5700G, 5600G and 5300G APUs.
- AMD Radeon RX 6900 XT, 6800 (& XT), 6700 XT GPUs.

--------------------------------------------------------------------------------------------------
1.95 - January 2021
- LPDDR4 and LPDDR4X memory SPD.
- AMD Cezanne and Lucienne APUs.
- AMD ThreadRipper PRO 3995WX, 3975WX, 3955WX, 3945WX and WRX80 chipset.
- Intel Core 11th generation "Rocket Lake".
- Mainboard PCI-Express generation report (Mainboard tab).
- Graphics Interface Link current speed and max speed (Mainboard tab).
- NVIDIA GPU base and boost clocks (Graphics tab).

--------------------------------------------------------------------------------------------------
1.94.8 - November 2020
- Intel Tiger Lake-U and Tiger Lake-H (1.94.8).
- Intel Z590 (1.94.8).
- NVIDIA RTX 3070 GPU (1.94.8).
- AMD Ryzen 5000 Vermeer preliminary support.
- Intel Rocket Lake preliminary support.
- GPU TDP.

--------------------------------------------------------------------------------------------------
1.93 - August 2020
- AMD Ryzen 7 PRO 4750G, Ryzen 5 PRO 4650G, Ryzen 3 PRO 4350G.
- Intel Tiger Lake platform.

--------------------------------------------------------------------------------------------------
1.92.2 - June 2020
- AMD Ryzen 9 3900XT, Ryzen 7 3800XT and Ryzen 5 3600XT (1.92.2).
- AMD B550 chipset (1.92.2).
- Hygon processors preliminary support (1.92.2).
- Intel Z490/W480/B460 Comet Lake platform support.
- AMD "Renoir" Ryzen APU.
- AMD Ryzen 3 3100 & 3300X.
- Windows 10 on ARM processor preliminary support.

--------------------------------------------------------------------------------------------------
1.91 - December 2019
- AMD Ryzen Threadripper 3960X, 3970X and 3990X.
- Intel Core 10xxx Comet Lake processors.
- Centaur Haul CHA w/ AI hardware preliminary support.

--------------------------------------------------------------------------------------------------
1.90 - August 2019
- AMD Ryzen 3000 PRO (1.90.1).
- Intel Comet Lake preliminary support (1.90.1).
- AMD Ryzen Threadripper 3000 preliminary support.
- Intel Ice-Lake preliminary support.
- NVIDIA RTX 2070 and 2080 Super.

--------------------------------------------------------------------------------------------------
1.89 - June 2019
- Preliminary support of AMD Ryzen 3000 (1.89.1)
- Intel Cascade Lake processors (1.89.1)
- Zhaoxin ZX-5000 and ZX-6000 processors families support.
- AMD APU Ryzen Picasso detection fix.

--------------------------------------------------------------------------------------------------
1.88 - March 2019
- Intel 9400F, 9600KF, 9700KF and 9900KF and 9th gen R0 stepping.
- AMD APU Ryzen Picasso preliminary support.
- DDR4 SDRAM chips manufacturer.
- AMD Radeon VII (Vega 20).
- NVIDIA GTX 1660 and 1660 Ti.

--------------------------------------------------------------------------------------------------
1.87 - November 2018
- Intel Basin Falls Skylake-X refresh.
- NVIDIA GeForce RTX serie 20.
- New AVX2 and AVX512 benchmarks (beta versions).

--------------------------------------------------------------------------------------------------
1.86 - August 2018
- Intel 9th generation Core family (Coffee Lake 9900K, 9700K, 9600K, 9600, 9500 and 9400).
- Intel Coffee Lake-U processors.

--------------------------------------------------------------------------------------------------
1.85 - May 2018
- AMD AGESA version report in BIOS information.
- Increased clocks refresh rate.
- Fix initialization error on Windows XP and 7.

--------------------------------------------------------------------------------------------------
1.84 - March 2018
- Intel new Coffee Lake desktop and mobile processors.
- New timers tool.
- New "-bench" parameter.

--------------------------------------------------------------------------------------------------
1.83 - January 2018
- AMD desktop Raven Ridge APU (AM4).
- Windows 10 Build 16299.

--------------------------------------------------------------------------------------------------
1.82 - December 2017
- AMD Raven Ridge processors support (1.82.1)
- Intel Xeon Phi Knight Landing preliminary support.
- Fix BCLK fluctuations on SKL-X.
- The benchmark does now support systems with more than 64 CPUs.

--------------------------------------------------------------------------------------------------
1.81.1 - October 2017
- Intel Coffee Lake processors and Z370 platform.
- Intel Skylake-X HCC processors.
- Intel Xeon Skylake-SP and Xeon W Skylake processors.

--------------------------------------------------------------------------------------------------
1.80.2 - September 2017
- Intel Core X processors (KBL-X and SKL-X).
- Preferred core(s) display in clocks dialog.
- Fix 32-bit version issue with Windows 10 builds 16273 and 16275 (1.80.2)

--------------------------------------------------------------------------------------------------
1.79.1 - May 2017
- Intel X299 (1.79.1).
- AMD ThreadRipper (1.79.1).
- AMD Ryzen 5 and Ryzen 3.
- Fix lockup at loading on Ryzen systems with RAID.
- New benchmark version (17.01).

--------------------------------------------------------------------------------------------------
1.78.3 - February 2017
- AMD Ryzen (1.78.3)
- Intel Kaby Lake processors.
- AMD Embedded G and R-series processors.
- DMP Vortex86 DX3.
- Tons of bug fixes.

--------------------------------------------------------------------------------------------------
1.77 - August 2016
- New benchmark "submit and compare" feature.
- New clocks dialog reporting all system's clock speeds in real-time.
- Preliminary support for Intel Kaby Lake.
- AMD Bristol Ridge processors.

--------------------------------------------------------------------------------------------------
1.76 - April 2016
- Intel Broadwell-E/EP processors.
- AVX512 instruction set report.
- Fixed several bugs (missing SPD on some systems).

--------------------------------------------------------------------------------------------------
1.75 - January 2016
- Intel Skylake Pentium and Celeron support.
- Intel Broadwell-E preliminary support.
- AMD A10-7890K APU.

--------------------------------------------------------------------------------------------------
1.74 - October 2015
- Improved CPU benchmark.
- AMD Carrizo APUs.
- eDRAM detection on Slylake CPUs.

--------------------------------------------------------------------------------------------------
1.73 - August 2015
- New validation.
- eXtreme Overclock Mode with light CPU load.
- New tab : CPU Benchmark.
- Added L4 cache frequency (cache tab).
- Added Windows 10 editions.

--------------------------------------------------------------------------------------------------
1.72.1 - May 2015
- Intel Skylake and Broadwell support (1.72.1)
- Preliminary support for Intel Skylake.
- Fixed support for Windows 10 build 9926.

--------------------------------------------------------------------------------------------------
1.71.1 - November 2014
- Intel Core M processors (1.71.1)
- New CPU-Z logo (1.71.1)
- AMD Athlon X2 450, Athlon X4 840 and Athlon X4 860K (Kaveri).
- AMD FX-8370, FX-8370E, FX-8320E (Vishera).
- Improved support for Intel X99 chipset.
- Support for Microsoft Windows 10. 

--------------------------------------------------------------------------------------------------
1.70 - July 2014
- Intel i7-5960X, i7-5930K, i7-5820K, i7-4790K, i5-4690K, Pentium G3258.
- TSX instructions set support (including RTM and HLE subparts).

--------------------------------------------------------------------------------------------------
1.69.2 - April 2014
- Intel Haswell-E processors (1.69.2)
- Preliminary support for DDR4 memory (1.69.2)
- AMD FCH detection improved (1.69.2)
- AMD A6-6420K, A4-6320, A4-4020.
- AMD Athlon 5350 & 5150, Sempron 3850 & 2650 Kabini.
- Intel Core i7-4770R and Core i5-4570R Crystal Well.
- Added graphic card vendor.

--------------------------------------------------------------------------------------------------
1.68 - January 2014
- AMD Kaveri APUs (A10-7850K, A10-7800, A10-7700K, A8-7600, A6-7400K, A4-7300).
- Intel i7-4790, i5-4690, i5-4590, i5-4460 (Haswell refresh).
- Intel Celeron Haswell (G1830, G1820).
- Intel serie 9 chipset (Z97).
- Nuvoton NCT6106 and SMSC SCH5636 SIOs (Fujitsu mainboards).

--------------------------------------------------------------------------------------------------
1.67.1 - November 2013
- Fix several validations issues (1.67.1).
- Improved support of Intel Silvermont (Bay Trail).
- New validation.

--------------------------------------------------------------------------------------------------
1.66 - August 2013
- Intel Core i7-4xxx MQ/MX processors (1.66.1)
- Fixed BCLK jump bug and validation issue (1.66.1)
- Intel Xeon E5-2600 V2, Core i3-4xxx, Core i7-3910K processors.
- Intel Atom Bay Trail-T preliminary support.
- Intel Ivy Bridge-E/EP/EX support improved.
- AMD Opteron 3200 and 3300 series.
- ITE IT8603 and IT8623 SIOs (Asus FM2+ mainboards).
- Microsoft Windows 8.1 (Windows Blue). 
- New version checker.

--------------------------------------------------------------------------------------------------
1.65 - June 2013
- AMD Opteron X1150 and X2150.
- AMD FX-9590 and FX-9370.
- Intel 4xxxHQ "Crystalwell" CPUs.
- Intel Pentium "Haswell" G3430, G3420, G3220.

--------------------------------------------------------------------------------------------------
1.64 - April 2013
- Intel Atom Cloverview.
- Intel Ivy Bridge-E/EP/EX.
- AMD Richland APUs.

--------------------------------------------------------------------------------------------------
1.63 - February 2013
- Intel Pentium & Celeron "Ivy Bridge" CPUs.
- AMD Athlon X4 750K.
- FMA3 & 4 instructions sets.
- Support for systems with more than 64 CPUs.
- Report TDP in validation pages.

--------------------------------------------------------------------------------------------------
1.62 - October 2012
- AMD "Vishera" processors.
- Intel Haswell & Ivy Bridge E/EP preliminary support.
- Reduce CPU utilization.
- Improved validation process.

--------------------------------------------------------------------------------------------------
1.61 - July 2012
- Fix wrong clock speed report (1.61.3).
- Intel CedarTrail platform.
- VIA VX11 chipset.
- Intel Xeon E5 processors.
- Fix wrong CPU clock speed under Win8.

--------------------------------------------------------------------------------------------------
1.60 - March 2012
- Intel Core i5 2550K, 2450P and 2380P with no GPU.
- Intel Core i3/i5/i7 "Ivy Bridge" (37xx, 35xx, 34xx, 33xx, 32xx).
- Intel Xeon E3 (SandyBridge-WS).
- Intel Z77 platform.
- AMD FX-8140, FX-4150.
- AMD "Trinity" APU Preliminary support.
- Windows 32/64-bit information.

--------------------------------------------------------------------------------------------------
1.59 - November 2011
- AMD Opteron Interlagos and Valencia (Bulldozer).
- AMD SR56x0 I/O bridge and SP5100 southbridge (Maranello).
- Intel Core i7-3960X, 3930K and 3820 (SandyBridge-E).
- Intel X79 chipset (Jaketown).
- VIA Nano 1000/2000/3000, Eden X2, Nano X2/X3, QuadCore.
- Support for Windows 8.

--------------------------------------------------------------------------------------------------
1.58 - June 2011
- AMD FX "Bulldozer" processors family (K15).
- AMD Llano processors family (K12).
- Intel Sandy Bridge-EP processors preliminary support.

--------------------------------------------------------------------------------------------------
1.57 - February 2011
- Intel Xeon "Westmere-EX" support.
- GPU frequency report on Intel Sandy Bridge processor.
- AMD Zacate/Ontario processors support.
- Report TDP on main page when available.
- Cores clocks on floating menu.
- P67/H67 stepping report.

--------------------------------------------------------------------------------------------------
1.56 - October 2010
- Intel Core i3/i5/i7 "2000" serie processors (Sandy Bridge) with new logos.
- Intel Core i7 970 "Gulftown".
- Intel P67 chipset.
- AMD Phenom II, Athlon II and Sempron II "Caspian" processors.

--------------------------------------------------------------------------------------------------
1.55 - July 2010
- VIA VX900/VX900M and VN1000/VN1000M chipsets.
- Intel Core i5-760, Core i7-875K and Core i5-655K processors.
- Intel Core i7-940XM, 840QM, 740QM mobile processors.
- Intel Core i5-450M, 540UM, 430UM, Core i3-370M mobile processors.
- Intel Pentium P6000 processor.
- New parameter in cpuz.ini : UseDisplayAPI

--------------------------------------------------------------------------------------------------
1.54 - March 2010
- Intel Core i7-880, Core i5-680, Core i3-550 processors.
- Intel Xeon 5600 line (32nm) processors.
- AMD Phenom II X6, Sempron II, Turion II, Turion II Ultra and Athlon II Mobile "Caspian" processors.
- NVIDIA GeForce 320M chipset.
- AMD 870/880G/890GX + SB800/SB850 chipset.
- Introduction of validation rooms.

--------------------------------------------------------------------------------------------------
1.53 - December 2009
- Fix validation issue with AMD Phenom processors (1.53.1).
- Preliminary support for Intel Pinetrail platform (1.53.1).
- Intel Core i7 930 & 950, Core i7 980X "Gulftown", Xeon W3565.
- Intel Mobile Core i7/i5/i3 "Arrendale" (LV and ULV).
- Intel "Clarkdale" platform improved support. 
- AMD Phenom FX-5000, Athlon II X2/X3/X4, Athlon L110.
- AMD AM3 socket detection improved.
- VIA VX800 and VX855 chipsets support.
- ATI Radeon 2100 and Radeon X1250 support. 
- DirectX 11 detection fix.
- Hardware virtualization support now reported as instructions set.
- New HTML report with less information.

--------------------------------------------------------------------------------------------------
1.52 - July 2009
- Fix Core i7 clock bug (1.52.2)
- New design with "Graphics" tab.
- NVIDIA nForce 980a chipset support.
- Intel Core i5 and Core i3 processors.
- Intel Atom Z530, Pentium DC E6300 (2.8GHz) and Core 2 Solo processors.
- Intel P55 and US15W chipsets preliminary support.
- AMD Phenom TWKR support.
- AMD Phenom X2 "Callisto" and Athlon X2 "Regor" processors.
- AMD Opteron 6-core "Istanbul" processor.
- Improved support for Windows 7.
- New Intel logos.

MD5 checksum : please refer to http://www.cpuid.com/cpuz.php#history


--------------------------------------------------------------------------------------------------
1.51 - April 2009
- New version with installer.
- NVIDIA ION platform.
- Intel "Clarkdale" processor preliminary support.

MD5 checksum : please refer to http://www.cpuid.com/cpuz.php#history

--------------------------------------------------------------------------------------------------
1.50 - February 2009
- Improved support for AMD Phenom II processor.
- Support for NVIDIA 740i chipset.
- Several bug fixes.

MD5 cpuz.exe (1.50) : E1C827C54F527404EDDA6D65DA0280D8

--------------------------------------------------------------------------------------------------
1.49 - December 2008
- AMD Phenom II processor.
- New Intel Core 2 models.
- Improved loading time.
- New online validation process.

MD5 cpuz.exe (1.49) : B836E86B40D7AFAF99D9C512E8CC9FEF

--------------------------------------------------------------------------------------------------
1.48 - October 2008
- Improved support for Intel Core i7 (Bloomfield & Gainestown).
- Intel Atom dual-core version support.
- AMD Athlon "Kuma" processor support.
- Alternate clock computation method based on "duty cycles" for Intel Core 2 processors (see ShowDutyCycles in cpuz.ini)

MD5 cpuz.exe (1.48) : DB78FBF1F99B7709240B495E59758CCE

--------------------------------------------------------------------------------------------------
1.47 - August 2008
- Intel Core i7 processor.
- Intel Core 2 Duo S Mobile processors.
- NVIDIA MCP79/7A chipset.
- AMD 790GX / SB750 chipset. 

MD5 cpuz.exe (1.47) : A7484EFA3C32BC968C248CB1237FF493

--------------------------------------------------------------------------------------------------
1.46 - June 2008
- AMD Puma platform (Turion & Turion Ultra "Griffin");
- AMD Black Edition processors.
- Intel Xeon EX "Dunnington".

MD5 cpuz.exe (1.46) : C47432C41EC26156128F7B8E09E884CF

--------------------------------------------------------------------------------------------------
1.45 - May 2008
- Intel Atom Z500/Z510/Z520/Z530/Z540 processors.
- Intel Core 2 Duo E7x00 processor.
- AMD 45 nm Phenom (Dened & Shanghai) processors.
- Intel P45 chipset.

MD5 cpuz.exe (1.45) : DD4E540C04297CF93DC721166C13446E

--------------------------------------------------------------------------------------------------
1.44.2 - Mars 2008
- NVIDIA 650i, 680i, 750a/780a, 750i, 780i, 790i chipsets (1.44.2)
- Intel G35 chipset (1.44.2)
- Updated support for VIA chipsets (1.44.2)
- Support for the new validator (1.44.1)
- Preliminary support for VIA Isaiah processor.
- Support for all VIA Model D processors: VIA C7-M, VIA C7-D, VIA Eden, VIA Eden.
- Memory timings on Intel 5400A/B MCH (Skulltrail).
- Fix AMD Phenom detection issue on Windows Vista 64.

MD5 (1.44.2) : 81FB48A6EC7355E8D0DD71ADC2157CF6

--------------------------------------------------------------------------------------------------
1.43 - January 2008
- Intel Skulltrail platform support (Core 2 Extreme QX9775 + 5400B MCH).
- Intel X48 chipset detection.
- AMD Athlon 64-LE 1600 and 1620 support.
- Fix detection issues on unlocked Phenom (ES/BE).

--------------------------------------------------------------------------------------------------
1.42 - November 2007
- Intel Wolfdale/Yorkfield/Penryn CPUs support.
- AMD Phenom and Opteron Barcelona support improved.
- Current core selection menu (right click on the CPU page).
- Loading progress.

--------------------------------------------------------------------------------------------------
1.41 - September 2007
- New CPU voltage monitoring.
- Intel Xeon Tigerton support.
- Intel IA64 CPUs support (Itanium / Itanium2)(special thanks to Rami Radi and Ray Hinchliffe).
- AMD K10 Phenom preliminary support.
- NVIDIA MCP73 support.
- Intel XMP profiles support.
- Memory modules nominal voltage.

--------------------------------------------------------------------------------------------------
1.40.5 - July 2007
- NVIDIA nForce 560 chipset support.
- Intel Dynamic FSB technology support.
- Athlon 64 X2 BE & Sempron LE (TDP 45W).
- SiS671/FX/DX/MX northbridge support.
- SiS968 southbridge support.

--------------------------------------------------------------------------------------------------
1.40 - May 2007
- VIA P4M800CE chipset support.
- Preliminary support for Intel Penryn CPUs family (45 nm).
- Intel Core 2 Duo E6x20, Pentium E2140/2160 processors recognition.
- Intel P35, G33, G31, Q35, Q33, X38 chipsets support.
- Intel GL960/GM965/PM965 chipsets support.
- AMD Athlon 64 "Lima" processor support.
- AMD Geode LX processor support.
- NVIDIA nForce 520 chipset support.
- New parameter "-console" to generate output in a command prompt (Windows XP only).
- New icon.

--------------------------------------------------------------------------------------------------
1.39 - February 2007
- Windows Vista 64 support.
- VIA CX700/VX700 chipset.	
- Added Merom XE (X7900/X7800)
- Added "PCI" and "MaxPCIBus" in the ini file to configure PCI scanning.
- Added "-core" parameter to display the clock speed of the specified core.
- Lot of bug fixes.

--------------------------------------------------------------------------------------------------
1.38 - November 2006
- VIA P4M890 and P4M900 chipsets.
- NVIDIA 650i and 680i chipset.
- AMD Athlon 64 FX-7x processor.
- Core 2 Duo E6850, E6750, E6650 processors.
- socket 775 Xeon processor.
- New parameters "-txt" and "-html" (see above).
- Lot of bug fixes.

--------------------------------------------------------------------------------------------------
1.37 - October 2006
- Intel Core 2 Quad, Celeron 360, Mobile Core 2 Duo T5200 support.
- Changed SSE4 to SSSE3 (Supplemental SSE3).
- Added VIA VT8237A southbridge.
- New HTML report.
- Several bug fixes.

--------------------------------------------------------------------------------------------------
1.36 - August 2006
- New processor information display.
- Intel 5000X/P/Z/V SPD support.
- Intel Pentium D 925 & 945.

--------------------------------------------------------------------------------------------------
1.35 - June 2006
- ATI RS350, RS400, RS480/RX480, RS482, RD580/RX580, RS600/RD600, RS690, RS700 chipsets support.
- ATI SB600 southbridge support.
- Intel P965 chipset support.
- Engineering samples Core based CPUs report.
- Windows Vista product line report.

--------------------------------------------------------------------------------------------------
1.34.1 - May 2006
- AMD socket AM2/S1/F (rev F.) CPUs support.
- Enhanced Performance Profile (EPP) memory information report.
- Added Trc (bank cycle time) in DDR2 SPD report.
- Added Trc (bank cycle time) on i925, i945, i955, i975 chipsets.
- FB-DIMM DDR2 SPD information report.
- Core 2 Duo names and logos added.
- Turion 64 X2 logo added.
- Several bug fixed.

--------------------------------------------------------------------------------------------------
1.33.1 - April 2006
- Intel i965 chipset support added.
- Fix SPD detection problem on SiS 964/965/966 southbridges.
- ODC (Odd Divisor Correct) for Athlon64 rev E.

--------------------------------------------------------------------------------------------------
1.33 - April 2006
- Memory Command Rate timing added.
- Improved support for Intel Conroe/Merom CPUs.
- Celeron M 420/430 Yonah.
- Xeon Dual core Dempsey.
- VIA C7-M CPU detection added.
- VIA PT880, PT880 Pro, K8T900 chipsets support.
- nVidia nForce 550/560/570 (MCP55) chipset support.
- VIA VT8251 southbridge support.
- Fixed HyperTransport information misreports (in dump file).
- Added K6-2 and K6-III logos.

--------------------------------------------------------------------------------------------------
1.32.1 - March 2006
- New Intel logos.
- Added Pentium D 925, Pentium EE 965.
- Several bugs fixed.

--------------------------------------------------------------------------------------------------
1.32 - February 2006
- Athlon FX-60 detection added.
- Athlon64 new memory dividers.
- Celeron 35x Cedar Mill, Pentium D 805.
- Core Solo & Core Duo detection improved.
- VIA C7 CPU detection added.
- Intel i975X, i945GT chipsets support.
- ATI RD580 chipset support.
- ULi smbus support improved.
- Fix smbus conflict with ClockGen.

--------------------------------------------------------------------------------------------------
1.31 - November 2005
- Improved support for ATI RS480/482 chipsets.
- GeForce 6100/6150 (nForce 410/430) chipsets support.
- Intel E7520 chipset support.
- New CPUs support : AMD Opteron socket 939, Intel Pentium 4 Cedar Mill and Presler,
Intel Xeon Paxville, Intel Pentium M Yonah SC & DC.
- Several bug fixed.

--------------------------------------------------------------------------------------------------
1.30 - August 2005
- Latest Sempron and Turion support improved.
- Intel Xeon Potomac, Pentium M 780 support.
- Improved Windows version report.
- Support for SMBus on latest SiS bridges (964, 965, 966) and ULi bridges.
- Increased clocks refresh rate.
- Several bug fixed.

--------------------------------------------------------------------------------------------------
1.29 - June 2005
- New PCI devices report.
- Switch for VCore report in .ini file.
- VCore report on some uGuru mainboards.
- Improved clocks computation.
- Improved support for latest AMD CPUs, Celeron D 3x1 (EM64T).
- Improved support for SiS 649, 650 and 656 chipsets, Intel 915PM chipset.
- Improved stability when several instances of CPU-Z are running in the same time.
- Restored html report in ghost mode (see "Parameters" chapter above).

--------------------------------------------------------------------------------------------------
1.28.6 - March 2005
- Support for Athlon 64 X2 CPUs
- Support for i955X chipset
- Bunch of bugs

--------------------------------------------------------------------------------------------------
1.28 - March 2005
- Support for SiS chipsets improved : 648FX, 649, 655FX, 655TX, 656
- Support for nVidia nForce4 SLI Intel Edition
- Support for Intel Pentium D, Pentium XE, AMD Turion, Opteron 252
- New F7 key to simplifiy the creation of the validation file

--------------------------------------------------------------------------------------------------
1.27 - February 2005
- Support for Intel Pentium 4 6xx CPUs.
- New validation system.
- Hundred of bug fixes.

--------------------------------------------------------------------------------------------------
1.26 - December 2004
- VCore display on Pentium M CPUs.
- New CPU clock computation.
- Improved support of Transmeta CPUs.
- Several bugs fixed.

--------------------------------------------------------------------------------------------------
1.25 - November 2004
- nVidia nForce4 chipset support
- Intel Pentium 4J support
- New button to refresh memory timings
- Several bugs fixed

--------------------------------------------------------------------------------------------------
1.24 - September 2004
- AMD Sempron support
- New memory SPD page
- PCI-Express graphic interface support
- Configuration file
- External cache latency tool
- Restored html report
- Fix several bugs

--------------------------------------------------------------------------------------------------
1.23 - June 2004
- Intel i915P/G, i925X chipsets support
- Intel LGA775 CPUs support
- Intel's CPU number display
- New AMD Athlon 64 steppings
- New report in one file

--------------------------------------------------------------------------------------------------
1.22 - April 2004
- New CPUs : Xeon Nocona, Celeron Prescott
- DDR2 memory support
- Clock mode when iconized

--------------------------------------------------------------------------------------------------
1.21 -  January 2004
- one file package
- Windows 64 support
- new chipsets support
- new CPU support : Celeron M, Pentium 4 "Prescott"

--------------------------------------------------------------------------------------------------
1.20a -  October 2003
- memory frequency and ratio on nForce2
- KT600 support
- W83627THF VRM mode
- SPD read improvement
- Fix some memory leaks (DMI)
- Fix memory size report
- Fix AGP side band status

--------------------------------------------------------------------------------------------------