# ZenStates-Core
This project aims to provide a common functions for full SMU control on AMD Zen platform.  
The library is used in ZenTimings and ZenStates projects.

## CPU Support
The following AMD Zen-based codenames are supported
* SummitRidge
* Whitehaven
* Naples
* RavenRidge
* PinnacleRidge
* Colfax
* Picasso
* FireFlight
* Matisse
* CastlePeak
* Rome
* Dali
* Renoir
* VanGogh
* Vermeer
* Chagall
* Milan
* Cezanne
* Rembrandt
* Lucienne
* Raphael
* Phoenix
* Phoenix2
* Mendocino
* Genoa
* StormPeak
* DragonRange
* Mero
* HawkPoint
* StrixPoint
* GraniteRidge
* KrackanPoint
* StrixHalo
* Turin
* Bergamo

## How To
First, reference the DLL in your .NET project and import it.
```c#
using ZenStates.Core;
```
Then initialize it. Cpu is the main module.
```c#
private readonly Cpu cpu = new Cpu();
```

## Modules
### `Cpu.info`

| field          | type        |
|----------------|-------------|
| cpuid          | uint        |
| family         | Family      |
| codeName       | CodeName    |
| cpuName        | string      |
| packageType    | PackageType |
| baseModel      | uint        |
| extModel       | uint        |
| model          | uint        |
| ccds           | uint        |
| ccxs           | uint        |
| coresPerCcx    | uint        |
| cores          | uint        |
| logicalCores   | uint        |
| physicalCores  | uint        |
| threadsPerCore | uint        |
| patchLevel     | uint        |
| coreDisableMap | uint        |
| svi2           | SVI2        |


where svi2 exposes the following fields corresponding to the SVI2 power planes

| field          | type        |
|----------------|-------------|
| coreAddress    | uint        |
| socAddress     | uint        |

### `Cpu.systemInfo`

The module combines `Cpu.info` with additional information about the motherboard

## Projects used
[ryzen_smu (gitlab)](https://gitlab.com/leogx9r/ryzen_smu/)  
[ryzen_nb_smu (github)](https://github.com/flygoat/ryzen_nb_smu)  
[zenpower (github)](https://github.com/ocerman/zenpower)  
[Linux kernel (github)](https://github.com/torvalds/linux)  
[AMD's public documentation](https://www.amd.com/en/support/tech-docs)  
[Open Hardware Monitor](https://github.com/openhardwaremonitor/openhardwaremonitor)

with the help of patrickschur, leogx9r, PJVol, FlyGoat, sbski, 1usmus
