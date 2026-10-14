# Hub AceleraX (sincronizador ERP ↔ AceleraX)

Sincronizador entre o **ERP Oracle** (Senior Agribusiness / SIAGRI) e o app **AceleraX**,
escrito **100% em .NET 8**, num **único serviço do Windows**. Mesma arquitetura de leitura
de banco, geração de versão e deploy do **HubUniSeeds**.

Um projeto (`src/AceleraXSync`) → um exe `HubAceleraX.exe` → um serviço `HubAceleraX` (x86),
self-contained (a máquina de destino não precisa de .NET), que roda **dois workers** no mesmo
processo:

| Worker | Direção |
|---|---|
| **Carga de base** | ERP → AceleraX (`POST /api/v1/integracao/base/<slug>`). |
| **Pedidos** | AceleraX → ERP, via DLL `agb/AGBPedidoVenda.dll` (32-bit → processo x86). |

> **Nome:** o serviço/produto chama-se **HubAceleraX** — não `HubPadrao`/`Hub2LN`, que é
> outro produto. Os dois podem conviver na mesma máquina. Por dentro, o namespace/pasta é
> `AceleraXSync` (como o HubUniSeeds mantém `HubPadrao.Api` e só o produto vira `Hub2LN`).

> **Autenticação:** header `X-Api-Key` (uma por tenant). O `codi_ten` é resolvido no
> servidor a partir da chave — o sync não envia tenant e não usa credencial de banco do app.

---

## Leitura do ERP: views ou consultas por cliente

- **Modo view** (cliente original): lê as views `V_AX_*` do schema.
- **Modo consulta** (`Sync:ConsultasDir`): roda `consultas/<cliente>/<entidade>.sql`, sem
  criar views no ERP — usado quando o ERP do cliente não tem as views (ex.: **Rural Forte**,
  schema `RURALF`). Ver `src/AceleraXSync/consultas/README.md`.

A leitura usa **Dapper** (dinâmica, `Rows.D`) e conexão em **modo ponte** (só Schema + DSN;
a senha do schema vem do `CADUSU` do próprio ERP) — igual ao HubUniSeeds.

---

## Build e deploy

```powershell
pwsh build\build.ps1 -Version 1.0.0            # self-contained + instalador
pwsh build\build.ps1 -Version 1.0.0 -SkipInstaller
```

Pré-requisitos de build: **.NET 8 SDK**, o feed local `2ln-local` com o pacote
`2LN.Licensing` (`nuget.config`) e, para o instalador, **Inno Setup 6**.

Resultado:
- `build\HubAceleraX-<versão>.zip`
- `installer\Output\HubAceleraX-Setup-<versão>.exe`

O instalador pergunta a conexão Oracle (Schema/DSN), a API AceleraX (ApiUrl/ApiKey) e a pasta
de consultas; escreve o `appsettings.Production.json`, cria e inicia o serviço, e migra
instalações antigas (quando eram dois serviços separados). Alternativa manual:
`deploy\INSTALAR.bat` (após preencher o `appsettings.Production.json`).

O AGB.ini do ERP do cliente vai na pasta `agb\` (não acompanha o pacote).

---

## Rodar em desenvolvimento

```bash
dotnet run --project src/AceleraXSync
```

Com `DOTNET_ENVIRONMENT=Development` e um `appsettings.Development.json` (gitignored)
preenchido. Para validar a carga de base sem enviar nada: `Sync:DryRun=true` lê e transforma
tudo e grava `dump/<entidade>.json` **sem** chamar a API.

---

## Entidades da carga de base

Ordem (`TablesSpec.SyncOrder`). Cada entidade vira um `POST /api/v1/integracao/base/<slug>`;
a chave de upsert é resolvida no servidor.

| Entidade | Slug | Chave de upsert |
|---|---|---|
| empresa | `empresas` | `(codi_ten, codi_emp)` |
| vendedor | `vendedores` | `(codi_ten, codi_pes)` |
| categorias¹ | `categorias` | `(codi_ten, codi_cca)` |
| transac (clientes) | `clientes` | `(codi_ten, codi_tra)` |
| propried | `propriedades` | `(codi_ten, prop_pro)` |
| grupo | `grupos` | `(codi_ten, chavegrupo)` |
| prodserv | `produtos` | `(codi_ten, codi_psv)` |
| ciclos_safra | `ciclos` | `(codi_ten, codi_cic)` |
| estoque | `estoque` | `(codi_ten, codi_pro, codi_fil)` |
| tabelas_preco + itens | `tabelas-preco` | `(codi_ten, erp_id)` / `(…, codi_pro)` |
| condicao | `condicoes` | `(codi_ten, cond_con)` |
| carteira | `carteira` | `(codi_ten, origem_erp_id)` |

> ¹ `categorias` e algumas colunas (`CODI_SUP` do vendedor, `PRECO`→`prrs_itp` dos itens)
> só existem no **modo consulta** — ver `TablesSpec.cs` (flag `SoConsulta`).

A **tabela de preço** vai cabeçalho + itens no mesmo endpoint; o servidor resolve
`erp_id → codi_tab` e reporta itens órfãos.

---

## Documentação por tema

- `CLAUDE.md` — arquitetura e comandos (guia de desenvolvimento).
- `src/AceleraXSync/README.md` — detalhes da carga de base.
- `src/AceleraXSync/consultas/README.md` — consultas por cliente (AgroSanta, Rural Forte).
