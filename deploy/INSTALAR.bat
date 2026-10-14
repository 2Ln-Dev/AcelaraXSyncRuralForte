@echo off
setlocal
REM ============================================================================
REM  Hub AceleraX - instala o servico do Windows a partir desta pasta.
REM  Um unico servico roda os dois fluxos (carga de base + pedidos).
REM  x86 (grava o pedido no ERP pela DLL AGB 32-bit). Self-contained -> a
REM  maquina NAO precisa de .NET.
REM  >>> Rode como ADMINISTRADOR <<<
REM ============================================================================

set BASE=%~dp0
set SERVICO=HubAceleraX

if not exist "%BASE%HubAceleraX.exe" ( echo ERRO: HubAceleraX.exe nao encontrado nesta pasta. & pause & exit /b 1 )
if not exist "%BASE%appsettings.Production.json" (
  echo ERRO: appsettings.Production.json nao encontrado.
  echo   1^) Copie appsettings.Production.exemplo.json para appsettings.Production.json
  echo   2^) Preencha Oracle ^(Schema/DSN^), Acelera ^(ApiUrl/ApiKey^) e Sync ^(ConsultasDir^).
  pause & exit /b 1
)
if not exist "%BASE%agb\AGB.ini" (
  echo AVISO: agb\AGB.ini nao encontrado. Coloque o AGB.ini do ERP do cliente na
  echo        pasta agb\ ^(ver agb\LEIA-AGB.txt^), senao a gravacao de pedido no ERP falha.
)

echo ==^> Parando/removendo servicos anteriores (se houver)
sc stop %SERVICO% >nul 2>&1
sc delete %SERVICO% >nul 2>&1
REM Instalacao anterior, quando eram DOIS servicos separados:
sc stop AceleraXBaseSync  >nul 2>&1
sc stop AceleraXPedidoErp >nul 2>&1
sc delete AceleraXBaseSync  >nul 2>&1
sc delete AceleraXPedidoErp >nul 2>&1

echo ==^> Criando servico %SERVICO%
sc create %SERVICO% binPath= "\"%BASE%HubAceleraX.exe\"" start= auto DisplayName= "Hub AceleraX"
sc description %SERVICO% "Hub AceleraX - sincronizador ERP <-> AceleraX (.NET + DLL AGB)."
sc failure %SERVICO% reset= 60 actions= restart/5000/restart/5000/restart/5000

echo ==^> Iniciando servico
sc start %SERVICO%

echo.
echo OK. Servico instalado. Logs em logs\hubacelerax-*.log
echo.
pause
