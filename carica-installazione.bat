@echo off
REM PRIMA INSTALLAZIONE: compila e carica sul server pacchetto + script di installazione.
REM Poi l'installazione va lanciata sul server (le istruzioni compaiono alla fine).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\carica-installazione.ps1"
pause
