@echo off
REM Aggiorna il server ATEC PM in azienda (192.168.2.150) con l'ultima versione del codice.
REM Prima esegue i test automatici: se falliscono, il server non viene toccato.
REM I test si saltano da soli se il codice C# non e' cambiato dall'ultima volta che erano verdi.
REM Spedisce solo i file cambiati. Per spedire comunque tutto: aggiorna-server.bat -Completo
REM Per rifare i test anche se il C# non e' cambiato:  aggiorna-server.bat -ConTest
REM In emergenza, per pubblicare saltando i test:   aggiorna-server.bat -SenzaTest
REM npm build si salta se il client non e' cambiato. Per rifarlo:  aggiorna-server.bat -ConClient
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\aggiorna-server.ps1" %*
pause
