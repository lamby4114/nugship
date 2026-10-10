@echo off
rem NUGSHIP dev server for phone testing (Windows). Read-only on chain: never signs or sends transactions.
rem   run_dev.cmd          -> live mainnet ORE rounds (public RPC, read-only)
rem   run_dev.cmd replay   -> real rounds replayed at 10x (fast testing)
rem Phone: adb reverse tcp:8787 tcp:8787   (game default ws://127.0.0.1:8787/ws)
cd /d "%~dp0"
if not exist node_modules call npm install
if not exist dist\src\index.js call npm run build
set PORT=8787
set ALLOW_GUEST_RANKED=true
if /I "%1"=="replay" (set ROUND_SOURCE=replay) else (set ROUND_SOURCE=live)
node dist\src\index.js
