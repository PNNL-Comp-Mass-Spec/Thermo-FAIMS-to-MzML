@echo off

set ExePath=ThermoFAIMStoMzML.exe

if exist %ExePath% goto DoWork
if exist ..\%ExePath% set ExePath=..\%ExePath% && goto DoWork
if exist ..\Bin\%ExePath% set ExePath=..\Bin\%ExePath% && goto DoWork

echo Executable not found: %ExePath%
goto Done

:DoWork
echo.
echo Processing with %ExePath%
echo.

%ExePath% QC_TD_Lumos01_Misc_21Oct20_Aragorn_18-12-13_5.raw

:Done

pause
