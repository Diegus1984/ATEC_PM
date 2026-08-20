@echo off
REM Esegue i test automatici QUI sul PC, fuori dal deploy, mentre continui a lavorare.
REM Se sono verdi lo registra: il prossimo aggiorna-server.bat non li rifa' e parte diretto.
REM Per rifarli anche se sono gia' verdi:        prova-test.bat -Comunque
REM Per provarne solo una parte (non registra):  prova-test.bat -Filtro DdpDaVerificare
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\prova-test.ps1" %*
pause
