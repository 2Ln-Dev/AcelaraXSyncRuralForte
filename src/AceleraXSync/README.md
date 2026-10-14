# Carga de base (ERP Ã¢â€ â€™ AceleraX) Ã¢â‚¬â€ worker `BaseSyncWorker`

O fluxo de **carga de base** do serviÃ§o **HubAceleraX** (projeto `AceleraXSync`), no padrÃƒÂ£o do
**HubUniSeeds** (`2Ln-Dev/HubUniSeeds`): Worker + Serilog + Options + `OracleConnectionFactory`
(modo ponte) + leitura via **Dapper** + licenÃƒÂ§a 2LN + Windows Service. O mesmo processo roda
tambÃƒÂ©m o `PedidoWorker` (pedidos AceleraX Ã¢â€ â€™ ERP).

## Fluxo

```
Oracle (views V_AX_*  OU  consultas/<cliente>/<entidade>.sql)
   Ã¢â€ â€™ OracleReader    Dapper (QueryAsync) + Rows.D (dicionario dinamico)
                     modo VIEW: SELECT <colunas do spec> FROM <view>   (full sync, sem WHERE)
                     modo CONSULTA: roda o .sql do cliente (Sync:ConsultasDir)
   Ã¢â€ â€™ Transformer     De/Para + tipos (datas ISO, decimal inteiro Ã¢â€ â€™ int)
   Ã¢â€ â€™ BaseApiClient   POST /api/v1/integracao/base/<slug>  (X-Api-Key, chunks)
   Ã¢â€ â€™ AceleraX (API)  Ã¢â€ â€™ UPSERT no Supabase do SaaS
```

O `codi_ten` **nÃƒÂ£o** vai no payload Ã¢â‚¬â€ ÃƒÂ© resolvido no servidor pela API key.

## Componentes

| Arquivo | Papel |
|---|---|
| `Sync/BaseSyncWorker.cs` | Orquestra `SyncOrder`; one-shot ou laÃƒÂ§o. |
| `Sync/OracleReader.cs` | LÃƒÂª o ERP com Dapper + `Core/Data/Rows.cs` (modo view/consulta). |
| `Sync/Transformer.cs` | De/Para OracleÃ¢â€ â€™destino + conversÃƒÂ£o de tipos. |
| `Acelera/BaseApiClient.cs` | `POST /integracao/base/<slug>` (X-Api-Key, em lotes). |
| `Sync/TablesSpec.cs` | Fonte da verdade: view, slug e De/Para por entidade. |
| `Core/Data/OracleConnectionFactory.cs` | ConexÃƒÂ£o Oracle (modo ponte), padrÃƒÂ£o HubUniSeeds. |
| `Core/Options.cs` + `appsettings*.json` | ConfiguraÃƒÂ§ÃƒÂ£o. |
| Serilog | console + `logs/base-sync-*.log`. |

## ConfiguraÃƒÂ§ÃƒÂ£o

`appsettings.json` traz os defaults; o ambiente vai em arquivo **gitignored**:

- Dev: `appsettings.Development.json` (rodar com `DOTNET_ENVIRONMENT=Development`).
  Parta do `appsettings.Development.example.json`.
- Cliente: `appsettings.Production.json` Ã¢â‚¬â€ sem senha do Oracle (modo ponte: sÃƒÂ³
  `Oracle:Schema` + `Oracle:Dsn`), igual ao HubPadrao.

> **`Oracle:Dsn` ÃƒÂ© sempre explÃƒÂ­cito** (EZConnect `host:porta/serviÃƒÂ§o`). A mÃƒÂ¡quina de
> desenvolvimento tem `ORACLE_SID=XE` como variÃƒÂ¡vel global; o driver gerenciado nÃƒÂ£o a
> consulta, e a configuraÃƒÂ§ÃƒÂ£o nunca deve depender dela.

SeÃƒÂ§ÃƒÂ£o `Sync`:

| Chave | Default | Papel |
|---|---|---|
| `BatchSize` | 1000 | registros por chunk (a API aceita atÃƒÂ© 5000; grava em lotes de 500) |
| `RunOnce` | true | uma rodada e encerra; false = loop |
| `IntervalSeconds` | 3600 | intervalo do modo loop |
| `DryRun` | false | lÃƒÂª e transforma, grava `dump/<entidade>.json`, **nÃƒÂ£o** chama a API |
| `DumpDir` | `dump` | pasta dos dumps do DryRun |
| `Somente` | Ã¢â‚¬â€ | roda sÃƒÂ³ uma entidade (ex.: `empresa`) |
| `Limite` | Ã¢â‚¬â€ | mÃƒÂ¡ximo de linhas por entidade (teste controlado) |
| `ConsultasDir` | Ã¢â‚¬â€ | pasta das consultas do cliente (ex.: `consultas/ruralforte`); vazio = views `V_AX_*` |

### Modo view x modo consulta

- **View** (sem `ConsultasDir`): ÃƒÂ© o cliente original (Agroinsumos, schema `AGROINSU`).
  LÃƒÂª as views `V_AX_*` criadas no ERP (`sql/create_views.sql`).
- **Consulta** (`ConsultasDir` preenchido): para cliente em que nÃƒÂ£o se cria view no ERP.
  Cada entidade roda `consultas/<cliente>/<entidade>.sql`. Entidade sem arquivo nÃƒÂ£o ÃƒÂ©
  sincronizada, e coluna do contrato ausente da consulta fica fora do payload (o upsert nÃƒÂ£o
  a toca). Regras e pendÃƒÂªncias por cliente em [`consultas/README.md`](consultas/README.md).

## Rodar

```bash
dotnet run --project src/AceleraXSync
```

Com `DOTNET_ENVIRONMENT=Development` para usar o `appsettings.Development.json`.

O servico Windows chama-se `HubAceleraX` (um unico servico roda a carga de base e os pedidos).

## Comportamento (full sync)

- **Full sync**: sem incremental, sem DELETE, sem watermark/hash.
- **Ordem** (`SyncOrder`): empresa Ã¢â€ â€™ vendedor Ã¢â€ â€™ categorias Ã¢â€ â€™ transac Ã¢â€ â€™ propried Ã¢â€ â€™ grupo Ã¢â€ â€™
  prodserv Ã¢â€ â€™ ciclos_safra Ã¢â€ â€™ estoque Ã¢â€ â€™ tabelas_preco+itens Ã¢â€ â€™ condicao Ã¢â€ â€™ carteira.
  `categorias` (e as colunas `CODI_SUP` do vendedor e `PRECO` dos itens) sÃƒÂ³ existem no
  modo consulta: as views nÃƒÂ£o as tÃƒÂªm.
- **Tabela de preÃƒÂ§o**: header + itens no mesmo endpoint (`tabelas-preco`); os headers sÃƒÂ£o
  reenviados a cada chunk de itens; o servidor resolve `erp_id Ã¢â€ â€™ codi_tab` e reporta ÃƒÂ³rfÃƒÂ£os.
- **Retry**: 1 tentativa extra, 5 s, em rede/5xx/429 (API) e em erro de banco (Oracle).
- **Erros**: falha de Oracle pula a entidade; 401 interrompe a rodada; erro de chunk ÃƒÂ©
  logado e a rodada segue.
- **TransformaÃƒÂ§ÃƒÂµes**: datas em ISO 8601, nÃƒÂºmero sem parte fracionÃƒÂ¡ria vira inteiro, NULL
  permanece NULL, De/Para de margens FERTÃ¢â€ â€™Adubos, DEFÃ¢â€ â€™QuÃƒÂ­mico, ESPÃ¢â€ â€™NutriÃƒÂ§ÃƒÂ£o, SEMÃ¢â€ â€™Semente.
