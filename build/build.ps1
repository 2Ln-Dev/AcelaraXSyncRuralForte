<#
  build.ps1 - gera o pacote de distribuicao do AceleraX Sync (servico UNICO).

  Mesma arquitetura de versao/deploy do HubUniSeeds: um .exe UNICO self-contained
  (a maquina de destino NAO precisa de .NET), a versao e' injetada no publish
  (-p:Version) e o instalador e' Inno Setup (pergunta a conexao Oracle + a API,
  escreve o appsettings.Production.json, cria o servico Windows).

  O servico HubAceleraX (x86) roda os DOIS fluxos no mesmo processo:
    - carga de base   (ERP -> AceleraX)
    - pedidos         (AceleraX -> ERP, via DLL AGB 32-bit -> processo x86)

  A licenca 2LN e' EMBUTIDA no binario (Release define LICENCA_OBRIGATORIA).

  Pre-requisitos DE BUILD (nesta maquina):
    - .NET 8 SDK
    - feed local 2ln-local (nuget.config) com o pacote 2LN.Licensing
    - Inno Setup 6 (so' p/ o instalador; dispensavel com -SkipInstaller)

  Uso:
    .\build\build.ps1 -Version 1.0.0
    .\build\build.ps1 -Version 1.0.1 -SkipInstaller       (so' o zip)

  Resultado:
    build\HubAceleraX-<versao>.zip
    installer\Output\HubAceleraX-Setup-<versao>.exe
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipInstaller,
    # TEMPORARIO: publica sem o gate de licenca (nao define LICENCA_OBRIGATORIA).
    [switch]$SemLicenca
)

$ErrorActionPreference = "Stop"
$root    = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "build\publish"
$deploy  = Join-Path $root "deploy"
$proj    = Join-Path $root "src\AceleraXSync\AceleraXSync.csproj"
$agbSrc  = Join-Path $root "agb"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet (SDK .NET 8) nao encontrado no PATH." }

# ======================= 1) PUBLISH (x86, single-file) ======================
Write-Host "==> [1/4] Publish (Release, self-contained single-file win-x86, licenca EMBUTIDA)" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
$extra = @()
if ($SemLicenca) {
    Write-Host "    *** ATENCAO: pacote SEM validacao de licenca (-SemLicenca) ***" -ForegroundColor Yellow
    $extra += "-p:SemLicenca=true"
}
dotnet publish $proj `
    -c Release -r win-x86 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    @extra `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (exit $LASTEXITCODE)." }

# tira do pacote o que nao vai p/ o cliente
Get-ChildItem $publish -Include "appsettings.Development*.json", "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $deploy "appsettings.Production.exemplo.json") $publish -Force

# ======================= 2) DLL do ERP (agb\) ===============================
# So' a DLL (client-agnostica). O AGB.ini e' por-cliente e NAO acompanha o pacote.
Write-Host "==> [2/4] DLL do ERP (agb\AGBPedidoVenda.dll)" -ForegroundColor Cyan
$agbDll = Join-Path $agbSrc "AGBPedidoVenda.dll"
if (-not (Test-Path $agbDll)) { throw "agb\AGBPedidoVenda.dll nao encontrada em $agbSrc." }
$agbOut = Join-Path $publish "agb"
New-Item -ItemType Directory -Force -Path $agbOut | Out-Null
Copy-Item $agbDll $agbOut -Force
@"
Coloque AQUI o AGB.ini do ERP DESTE cliente (o mesmo do AGB/SIAGRI instalado na
maquina - ele aponta o Oracle do ERP). Ele NAO acompanha o pacote por ser
especifico do cliente. Sem o AGB.ini, a gravacao do pedido no ERP falha.

  AGBPedidoVenda.dll   DLL 32-bit que grava o pedido (vem no pacote)
  AGB.ini              <-- voce coloca (do ERP do cliente)
"@ | Out-File -FilePath (Join-Path $agbOut "LEIA-AGB.txt") -Encoding utf8

# ======================= 3) SCRIPTS + ZIP ===================================
Write-Host "==> [3/4] Scripts + pacote" -ForegroundColor Cyan
Copy-Item (Join-Path $deploy "INSTALAR.bat"), (Join-Path $deploy "REMOVER.bat"), (Join-Path $deploy "LEIAME.txt") $publish -Force
# As consultas por cliente vao EMBUTIDAS no exe (EmbeddedResource) - nada de .sql no pacote.
if (Test-Path (Join-Path $publish "consultas")) { throw "build\publish\consultas nao deveria existir: as consultas sao embutidas no exe." }

$zip = Join-Path $root "build\HubAceleraX-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "    pacote: $zip ($mb MB)" -ForegroundColor Green

# ======================= 4) INSTALADOR (Inno Setup) =========================
if (-not $SkipInstaller) {
    Write-Host "==> [4/4] Instalador (Inno Setup)" -ForegroundColor Cyan
    $cands = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $cands | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { $c = Get-Command ISCC -ErrorAction SilentlyContinue; if ($c) { $iscc = $c.Source } }
    if (-not $iscc) {
        throw "Inno Setup 6 nao encontrado (procurei em: $($cands -join ' | ') e no PATH). Instale (gratis) em https://jrsoftware.org/isdl.php e rode de novo (ou use -SkipInstaller)."
    }
    Write-Host "    ISCC: $iscc" -ForegroundColor DarkGray
    & $iscc "/DAppVersion=$Version" (Join-Path $root "installer\hubacelerax.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup (ISCC) falhou (exit $LASTEXITCODE)." }

    $setup = Join-Path $root "installer\Output\HubAceleraX-Setup-$Version.exe"
    $smb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "    instalador: $setup ($smb MB)" -ForegroundColor Green
} else {
    Write-Host "Instalador pulado (-SkipInstaller)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "OK. Build $Version concluido." -ForegroundColor Green
