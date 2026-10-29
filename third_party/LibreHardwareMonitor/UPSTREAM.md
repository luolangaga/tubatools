LibreHardwareMonitorLib is vendored from https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
at commit 1d74d0ea012cacbcdf858ab9c5dd8a862b25d6e6 (2026-10-29).
The library source and its build properties are kept alongside the upstream license
and third-party notices. The project file is restricted to net10.0 and omits the
upstream runtime list to avoid restoring unused target frameworks and runtimes.
For AnyCPU builds the project selects the runtime or host architecture for
CsWin32, whose generated interop requires a concrete architecture.
Update them together when refreshing this dependency.
