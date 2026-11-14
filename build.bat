@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
  /out:"bin\GhbgEditor.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "src\Program.cs"

if %ERRORLEVEL%==0 (
  echo [OK] bin\GhbgEditor.exe
) else (
  echo [FAIL] compile error
  exit /b 1
)
endlocal
