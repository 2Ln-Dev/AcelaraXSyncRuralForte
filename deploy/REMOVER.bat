@echo off
setlocal
REM Remove o servico do Hub AceleraX. Rode como ADMINISTRADOR.

set SERVICO=HubAceleraX

echo ==^> Parando servico %SERVICO%
sc stop %SERVICO% >nul 2>&1

echo ==^> Removendo servico %SERVICO%
sc delete %SERVICO%

echo.
echo OK. Servico removido. (A config appsettings.Production.json foi preservada.)
pause
