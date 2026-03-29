@echo off
echo Running setup via PowerShell...
powershell -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
pause
