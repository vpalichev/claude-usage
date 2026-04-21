@echo off
REM One-shot manual-login launcher. Opens Chromium in the dedicated profile
REM WITHOUT the grabber extension, so the window stays open while you log in.
REM Log in, confirm you can reach https://claude.ai/settings/usage, then close the window normally.

set CHROMIUM="C:\Program Files\Chromium\Application\chrome.exe"
set PROFILE_DIR=D:\projects\claude-usage\profile

%CHROMIUM% ^
  --user-data-dir="%PROFILE_DIR%" ^
  --no-first-run ^
  --no-default-browser-check ^
  "https://claude.ai/login"
