# CLAUDE.md

Guia para o Claude Code trabalhar neste repositório.

Sincronizador ERP ↔ AceleraX, **100% .NET 8**, num **único serviço do Windows**.
Mesma arquitetura de leitura de banco, versão e deploy do **HubUniSeeds** (`2Ln-Dev/HubUniSeeds`).

> **Nomes:** por dentro o namespace/pasta é `AceleraXSync`; o que o cliente vê (serviço,
> exe, produto) é **HubAceleraX** — mesmo critério do HubUniSeeds (namespace `HubPadrao.Api`,
> produto `Hub2LN`). **Não** reutilizar `HubPadrao`/`Hub2LN`: já existe esse serviço de outro
> produto nas máquinas.

## Comandos

```bash
dotnet build AceleraXSync.sln

# Rodar em dev (DOTNET_ENVIRONMENT=Development usa appsettings.Development.json)
dotnet run --project src/AceleraXSync

# Gerar o pacote de distribuição (self-contained + instalador Inno)
pwsh build/build.ps1 -Version 1.0.0
pwsh build/build.ps1 -Version 1.0.0 -SkipInstaller
```

> **Pré-requisito de build/restore:** o pacote `2LN.Licensing` vem do feed local
> `2ln-local` (`nuget.config` → `C:\Integracoes\_packages`). Sem ele o restore falha.

## Serviço único: HubAceleraX (x86)

Um projeto (`src/AceleraXSync`) → um exe `HubAceleraX.exe` → um Windows Service
`HubAceleraX`, que hospeda **dois workers** no mesmo processo:

| Worker | Arquivo | Fluxo |
|---|---|---|
| `BaseSyncWorker` | `Sync/BaseSyncWorker.cs` | **Carga de base**: ERP Oracle → API AceleraX (`POST /api/v1/integracao/base/<slug>`). |
| `PedidoWorker` | `Pedidos/PedidoWorker.cs` | **Pedidos**: AceleraX → ERP, via DLL `agb/AGBPedidoVenda.dll`. |

**x86 obrigatório:** a DLL AGB é 32-bit (P/Invoke in-process) → o processo tem que ser x86.
O Oracle gerenciado roda em x86, então a carga de base roda no mesmo processo.

Ambos autenticam na API com `X-Api-Key` (uma por tenant); o `codi_ten` vem do servidor.

> A carga de base em `RunOnce=true` **não** derruba o host (co-hospeda o PedidoWorker):
> só encerra o processo quando `DryRun=true` (atalho de teste). Em serviço, use
> `Sync:RunOnce=false` (laço).

## Leitura de banco — padrão HubUniSeeds

`Core/Data/OracleConnectionFactory.cs` + `Core/Security/{BridgeCredentials,ErpCrypto,SecretProtector}.cs`.
Dois modos: **direto (dev)** com `Oracle:User`+`Password`; **ponte (produção)** só `Oracle:Schema`+`Dsn`
(a senha do schema vem do `CADUSU` do ERP). Leitura dinâmica via **Dapper + `Core/Data/Rows.cs`** (`Rows.D`).

A carga de base lê por **view** (`V_AX_*`) ou por **consulta por cliente** (`Sync:ConsultasDir` = nome
do cliente → `consultas/<cliente>/<entidade>.sql`, **embutidas no exe** como EmbeddedResource —
o SQL não vai para o disco do cliente); ver `src/AceleraXSync/consultas/README.md`. Fonte da
verdade do De/Para: `Sync/TablesSpec.cs` (colunas `SoConsulta` só existem no modo consulta).

## Versão e deploy — padrão HubUniSeeds

- **Versão:** `build/build.ps1 -Version X.Y.Z` → `dotnet publish -p:Version=X.Y.Z`.
- **Pacote:** um `.exe` único self-contained win-x86 (a máquina de destino não precisa de .NET).
  A licença 2LN é embutida em Release (`LICENCA_OBRIGATORIA`); `-SemLicenca` publica sem o gate.
- **Instalador:** `installer/hubacelerax.iss` (Inno Setup) — pergunta Oracle (Schema/DSN),
  API (ApiUrl/ApiKey) e a pasta de consultas; escreve `appsettings.Production.json`; cria e
  inicia o serviço; migra instalações antigas de dois serviços. Headless (sem porta/firewall).
- **Deploy manual:** `deploy/{INSTALAR,REMOVER}.bat` + `appsettings.Production.exemplo.json`.

## Configuração

`appsettings.json` traz defaults; o real vai em arquivo **gitignored**
(`appsettings.Development.json` ou `appsettings.Production.json`). Seções:
`Acelera`, `Oracle`, `Sync` (carga de base), `Dll`/`Pedido` (pedidos), `Licenca`.

## Contrato de erro (BaseSyncWorker)

- `OracleReadException` → loga, pula a entidade, segue.
- `ApiAuthException` (401) → loga e interrompe a rodada.
- Erro de chunk → loga cada um, marca falha e segue.
