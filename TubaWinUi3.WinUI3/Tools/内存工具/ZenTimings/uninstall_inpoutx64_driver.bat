@echo off
@echo 1. Stopping service
sc stop inpoutx64

@echo.
@echo 2. Deleting service
sc delete inpoutx64

@echo.
@echo 3. Removing registry key
reg.exe delete "HKLM\SYSTEM\CurrentControlSet\Services\inpoutx64" /f

@echo.
@echo 4. Deleting driver
del C:\Windows\System32\drivers\inpoutx64.sys
pause