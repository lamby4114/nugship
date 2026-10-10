@echo off
rem Compile + offline self-test (needs an activated Unity license; unlicensed batchmode exits 198).
set UNITY="C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
set LOG=C:\Users\vdifu\Desktop\OreSeasLogs\compile.log
%UNITY% -batchmode -quit -nographics -projectPath "%~dp0.." -executeMethod OreSeas.EditorTools.BuildScript.CompileCheck -logFile "%LOG%"
echo exit code %ERRORLEVEL%  (0 ok, 1 compile errors, 2 self-test failed, 198 no license)
findstr /C:"[OreSeas]" /C:"error CS" "%LOG%"
