@setlocal
@pushd "%~dp0"
@for %%1 in ("%~n0") do @set "BASENAME=%%~n1"
@for /d %%1 in (%SystemRoot%\Microsoft.NET\Framework\v*)   do @if exist "%%~1\csc.exe" set "CSC=%%~1\csc.exe"
@for /d %%1 in (%SystemRoot%\Microsoft.NET\Framework64\v*) do @if exist "%%~1\csc.exe" set "CSC64=%%~1\csc.exe"
"%CSC%"   /target:exe /platform:x86 /optimize+ /warnaserror+ /out:"%BASENAME%.exe"   "%~n0"
"%CSC64%" /target:exe /platform:x64 /optimize+ /warnaserror+ /out:"%BASENAME%64.exe" "%~n0"
@popd
@endlocal
