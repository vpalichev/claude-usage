@echo off
REM One-shot manual-login launcher. Delegates to claude-usage.exe --login,
REM which uses the same Chromium-discovery and profile-path logic the tray
REM uses at scrape time. Log in at claude.ai, confirm you can reach
REM https://claude.ai/settings/usage, then close the browser window.
REM
REM Override Chromium discovery by setting CLAUDE_USAGE_CHROMIUM before running.

"%~dp0dist\claude-usage.exe" --login
