<#
  build.ps1 - gera o pacote de distribuicao do AceleraX Sync - Pedido ERP.

  Servico HEADLESS (sem frontend/porta). O resultado e um .exe UNICO
  self-contained win-x86 (a DLL AGBPedidoVenda e Delphi 32-bit -> P/Invoke
  in-process exige x86) - a maquina de destino NAO precisa de .NET. A licenca
  e EMBUTIDA no binario (Release define LICENCA_OBRIGATORIA).

  Pre-requisito DE BUILD (nesta maquina): .NET 8 SDK. Inno Setup 6 so p/ o
  instalador (dispensavel com -SkipInstaller).

  Uso:
    .\build\build.ps1 -Version 1.0.0
    .\build\build.ps1 -Version 1.0.1 -SkipInstaller   (so o zip, sem gerar o instalador)

  Resultado:
    build\AceleraXPedidoErp-<versao>.zip                        (exe + agb + scripts)
    installer\Output\AceleraXPedidoErpSetup-<versao>.exe        (instalador Inno)
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipInstaller,
    # TEMPORARIO: publica sem o gate de licenca (nao define LICENCA_OBRIGATORIA).
    # Continua sendo build Release/otimizado; so' a validacao de licenca fica de fora.
    # Sem esta flag o pacote sai como sempre, com a licenca embutida.
    [switch]$SemLicenca
)

$ErrorActionPreference = "Stop"
$root    = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "build\publish"
$deploy  = Join-Path $root "deploy"
$proj    = Join-Path $root "src\PedidoErp.Service\PedidoErp.Service.csproj"
$agbSrc  = Join-Path $root "agb"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet (SDK .NET 8) nao encontrado no PATH." }

# ======================= 1) BACKEND: publish (win-x86, single-file) ==========
Write-Host "==> [1/4] Publish (Release, self-contained single-file win-x86, licenca EMBUTIDA)" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
$extraArgs = @()
if ($SemLicenca) {
    Write-Host "    *** ATENCAO: pacote SEM validacao de licenca (-SemLicenca) ***" -ForegroundColor Yellow
    $extraArgs += "-p:SemLicenca=true"
}
dotnet publish $proj `
    -c Release -r win-x86 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    @extraArgs `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (exit $LASTEXITCODE)." }

# tira do pacote o que nao vai p/ o cliente
Get-ChildItem $publish -Include "appsettings.Development*.json", "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $deploy "appsettings.Production.exemplo.json") $publish -Force

# ======================= 2) DLL do ERP (agb\) ===============================
# So a DLL (client-agnostica). O AGB.ini e por-cliente (aponta ao ERP dele) e
# NAO acompanha o pacote - o operador coloca o do cliente. NAO copiar o AGB.ini
# de teste, o .bak nem o exe de teste.
Write-Host "==> [2/4] DLL do ERP (agb\AGBPedidoVenda.dll)" -ForegroundColor Cyan
$agbDll = Join-Path $agbSrc "AGBPedidoVenda.dll"
if (-not (Test-Path $agbDll)) { throw "agb\AGBPedidoVenda.dll nao encontrada em $agbSrc." }
$agbOut = Join-Path $publish "agb"
New-Item -ItemType Directory -Force -Path $agbOut | Out-Null
Copy-Item $agbDll $agbOut -Force
@"
Coloque AQUI o AGB.ini do ERP DESTE cliente (o mesmo do AGB/SIAGRI instalado
na maquina - ele aponta o Oracle do ERP). Ele NAO acompanha o pacote por ser
especifico do cliente. Sem o AGB.ini, a gravacao do pedido no ERP falha.

Arquivos desta pasta:
  AGBPedidoVenda.dll   DLL 32-bit que grava o pedido (vem no pacote)
  AGB.ini              <-- voce coloca (do ERP do cliente)
"@ | Out-File -FilePath (Join-Path $agbOut "LEIA-AGB.txt") -Encoding utf8

# ======================= 3) SCRIPTS + ZIP ==================================
Write-Host "==> [3/4] Scripts + pacote" -ForegroundColor Cyan
Copy-Item (Join-Path $deploy "INSTALAR.bat"), (Join-Path $deploy "REMOVER.bat"), (Join-Path $deploy "LEIAME.txt") $publish -Force

$zip = Join-Path $root "build\AceleraXPedidoErp-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "OK. Pacote: $zip ($mb MB) - 1 exe + agb + scripts (1 servico headless)." -ForegroundColor Green

# ======================= 4) INSTALADOR (Inno Setup) =========================
if (-not $SkipInstaller) {
    Write-Host "==> [4/4] Instalador (Inno Setup)" -ForegroundColor Cyan
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        throw "Inno Setup 6 nao encontrado ($iscc). Instale (gratis) em https://jrsoftware.org/isdl.php e rode de novo (ou use -SkipInstaller)."
    }
    & $iscc "/DAppVersion=$Version" (Join-Path $root "installer\pedidoerp-sync.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup (ISCC) falhou (exit $LASTEXITCODE)." }

    $setup = Join-Path $root "installer\Output\AceleraXPedidoErpSetup-$Version.exe"
    $smb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK. Instalador: $setup ($smb MB) - execute no cliente p/ instalar/atualizar." -ForegroundColor Green
} else {
    Write-Host "Instalador pulado (-SkipInstaller)." -ForegroundColor Yellow
}
