@echo off
setlocal
if not defined XAGENT_ENGINE_ROOT (
  >&2 echo X Agent CLI requires an active Rtl Terminal project session.
  exit /b 2
)
if not exist "%XAGENT_ENGINE_ROOT%\xagent.js" (
  >&2 echo X Agent CLI runtime is unavailable for this session.
  exit /b 2
)
where node >nul 2>nul
if errorlevel 1 (
  >&2 echo X Agent CLI requires Node.js 18 or newer on PATH.
  exit /b 9009
)
node -e "process.exit(Number(process.versions.node.split('.')[0]) >= 18 ? 0 : 1)" >nul 2>nul
if errorlevel 1 (
  >&2 echo X Agent CLI requires Node.js 18 or newer on PATH.
  exit /b 9009
)
node "%XAGENT_ENGINE_ROOT%\xagent.js" %*
set "XAGENT_EXIT=%ERRORLEVEL%"
endlocal & exit /b %XAGENT_EXIT%
