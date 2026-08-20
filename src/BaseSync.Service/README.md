# BaseSync.Service — carga de base (ERP → AceleraX) em .NET

Port do sincronizador Python (`sync.py` e módulos) para .NET 8, seguindo o padrão do
**HubPadrao** (`2Ln-Dev/HubPadrao`) e do irmão `PedidoErp.Service` deste mesmo repo.

- **HubPadrao** = como construir (Worker + Serilog + Options + `OracleConnectionFactory`
  com modo ponte + licença 2LN + Windows Service).
- **Python** = o que fazer (views, De/Para, ordem, chunks, retry, endpoint combinado).

O Python continua no repositório, intacto, como referência de validação.

## Fluxo

```
Oracle AGROINSU (views V_AX_*)
   → OracleReader    SELECT <colunas do spec> FROM <view>   (full sync, sem WHERE)
   → Transformer     De/Para + tipos (datas ISO, float inteiro → int)
   → BaseApiClient   POST /api/v1/integracao/base/<slug>  (X-Api-Key, chunks)
   → AceleraX (API)  → UPSERT no Supabase do SaaS
```

O `codi_ten` **não** vai no payload — é resolvido no servidor pela API key.

## Mapa Python → .NET

| Python | .NET |
|---|---|
| `sync.py` (orquestração) | `Sync/BaseSyncWorker.cs` (BackgroundService) |
| `oracle_reader.py` | `Sync/OracleReader.cs` (+ `Core/Data/OracleConnectionFactory.cs` do HubPadrao) |
| `transformer.py` | `Sync/Transformer.cs` |
| `api_writer.py` (+ `ENTITY_TO_SLUG`) | `Acelera/BaseApiClient.cs` (+ `Slug` no spec) |
| `tables_spec.py` (+ `SYNC_ORDER`) | `Sync/TablesSpec.cs` |
| `config.py` / `.env` | `Core/Options.cs` + `appsettings*.json` |
| `logger.py` / `sync.log` | Serilog → console + `logs/base-sync-*.log` |

## Configuração

`appsettings.json` traz os defaults; o ambiente vai em arquivo **gitignored**:

- Dev: `appsettings.Development.json` (rodar com `DOTNET_ENVIRONMENT=Development`).
  Parta do `appsettings.Development.example.json`.
- Cliente: `appsettings.Production.json` — sem senha do Oracle (modo ponte: só
  `Oracle:Schema` + `Oracle:Dsn`), igual ao HubPadrao.

> **`Oracle:Dsn` é sempre explícito** (EZConnect `host:porta/serviço`). A máquina de
> desenvolvimento tem `ORACLE_SID=XE` como variável global; o driver gerenciado não a
> consulta, e a configuração nunca deve depender dela.

Seção `Sync`:

| Chave | Default | Papel |
|---|---|---|
| `BatchSize` | 1000 | registros por chunk (a API aceita até 5000; grava em lotes de 500) |
| `RunOnce` | true | uma rodada e encerra (igual ao `sync.py`); false = loop |
| `IntervalSeconds` | 3600 | intervalo do modo loop |
| `DryRun` | false | lê e transforma, grava `dump/<entidade>.json`, **não** chama a API |
| `DumpDir` | `dump` | pasta dos dumps do DryRun |
| `Somente` | — | roda só uma entidade (ex.: `empresa`) |
| `Limite` | — | máximo de linhas por entidade (teste controlado) |

## Rodar

```bash
dotnet run --project src/BaseSync.Service
```

Com `DOTNET_ENVIRONMENT=Development` para usar o `appsettings.Development.json`.

Como serviço Windows o nome é `AceleraXBaseSync` (o `PedidoErp.Service` é
`AceleraXPedidoErp` — são dois serviços distintos).

## Comportamento preservado do Python

- **Full sync**: sem incremental, sem DELETE, sem watermark/hash.
- **Ordem** (`SyncOrder`): empresa → vendedor → transac → propried → grupo → prodserv →
  ciclos_safra → estoque → tabelas_preco+itens → condicao → carteira.
- **Tabela de preço**: header + itens no mesmo endpoint (`tabelas-preco`); os headers são
  reenviados a cada chunk de itens; o servidor resolve `erp_id → codi_tab` e reporta órfãos.
- **Retry**: 1 tentativa extra, 5 s, em rede/5xx/429 (API) e em erro de banco (Oracle).
- **Erros**: falha de Oracle pula a entidade; 401 interrompe a rodada; erro de chunk é
  logado e a rodada segue.
- **Transformações**: datas em ISO 8601, número sem parte fracionária vira inteiro, NULL
  permanece NULL, De/Para de margens FERT→Adubos, DEF→Químico, ESP→Nutrição, SEM→Semente.
