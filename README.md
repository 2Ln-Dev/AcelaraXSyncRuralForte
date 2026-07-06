# ERP → AceleraX Sync

Serviço Python standalone que sincroniza dados do Oracle ERP (Senior Agribusiness) para o **AceleraX Agro** **via API HTTP** (não mais direto no banco). Lê as tabelas-base via views no schema `AGROINSU` (`V_AX_*`), transforma as colunas conforme o De/Para da especificação e faz `POST` nos endpoints de integração do AceleraX, que executam o UPSERT idempotente.

> **Autenticação:** header `X-Api-Key` (uma chave por tenant). O `codi_ten` é
> resolvido no servidor a partir da chave — o sync **não** envia tenant. Assim o
> ERP não precisa mais de credencial do banco (`service_role`).

---

## Pré-requisitos

- Python 3.9+
- Oracle Instant Client instalado e no PATH (necessário para `cx_Oracle`)
- Acesso de rede ao banco Oracle e à **API publicada do AceleraX**
- Uma **`X-Api-Key`** da integração (gerada pelo time AceleraX, por tenant)
- Usuário `AGROINSU` com acesso às views `V_AX_*` (já criadas no próprio schema)

---

## Instalação

```bash
# 1. Crie e ative o ambiente virtual
python -m venv venv

# Windows
venv\Scripts\activate

# Linux / macOS
source venv/bin/activate

# 2. Instale as dependências
pip install -r requirements.txt

# Nota: cx_Oracle precisa ser instalado separadamente após configurar o Oracle Instant Client
pip install cx-Oracle==8.3.0
```

---

## Configuração

Copie o arquivo de exemplo e preencha com as credenciais reais:

```bash
copy .env.example .env
```

```env
# Oracle
ORACLE_HOST=<IP ou hostname do servidor Oracle>
ORACLE_PORT=1521
ORACLE_SID=<SID ou service name>
ORACLE_USER=hubraiz
ORACLE_PASSWORD=<senha>

# AceleraX API (escrita pela API, não mais direto no banco)
ACELERA_API_URL=https://app.seudominio.com.br   # base publicada, sem barra no fim
ACELERA_API_KEY=<chave X-Api-Key da integração>  # identifica o tenant

# Sync
SYNC_INTERVAL_SECONDS=3600
LOG_FILE_PATH=./sync.log
BATCH_SIZE=1000
LOG_LEVEL=INFO
```

> **Importante:** Nunca versione o arquivo `.env`. Ele já está no `.gitignore`.

---

## Execução

```bash
# Executar sincronização manualmente
python sync.py
```

O script termina com código `0` (sucesso) ou `1` (alguma tabela teve erro). Todos os eventos são gravados no arquivo de log configurado em `LOG_FILE_PATH`.

### Agendamento automático

**Windows — Task Scheduler:**
- Ação: `python C:\caminho\para\sync.py`
- Gatilho: intervalo desejado (ex: a cada 6 horas)

**Linux — cron:**
```bash
0 */6 * * * cd /opt/erp-sync && python sync.py >> /var/log/erp-sync.log 2>&1
```

---

## Tabelas sincronizadas (Onda 1)

A sincronização respeita a ordem de dependência de chaves estrangeiras:

| Ordem | View Oracle (AGROINSU) | Tabela Supabase | Chave UPSERT |
|-------|------------------------|-----------------|--------------|
| 1 | `V_AX_CLIENTES` | `transac` | `(codi_ten, codi_tra)` |
| 2 | `V_AX_PROPRIEDADES` | `propried` | `(codi_ten, prop_pro)` |
| 3 | `V_AX_GRUPOS` | `grupo` | `(codi_ten, codi_gpr)` |
| 4 | `V_AX_SUBGRUPOS` | `subgrupo` | `(codi_ten, codi_sbg)` |
| 5 | `V_AX_PRODUTOS` | `prodserv` | `(codi_ten, codi_psv)` |
| 6 | `V_AX_CICLOS` | `ciclos_safra` | `(codi_ten, nome_csa)` |
| 7 | `V_AX_ESTOQUE` | `estoque` | `(codi_ten, codi_pro, codi_fil, codi_csa)` |
| 8 | `V_AX_TABELA_PRECO` | `tabelas_preco` | `(codi_ten, nome_tab)` |
| 9 | `V_AX_TABELA_PRECO` | `itens_tabela_preco` | `(codi_tab, codi_pro)` |

---

## Arquitetura

```
sync.py              ← Orquestrador: carrega config, abre conexões,
                       itera as entidades (tabela de preço = header+itens)
   │
   ├─ oracle_reader.py   ← Lê dados das views Oracle (SELECT)
   ├─ transformer.py     ← Aplica mapeamento De/Para + conversão de tipos
   └─ api_writer.py      ← POST nos endpoints /api/v1/integracao/base/** (X-Api-Key)

tables_spec.py       ← Fonte da verdade: views, tabelas destino,
                       chaves UPSERT e mapeamento de colunas por tabela
api_writer.py        ← Também mapeia entidade → slug do endpoint (ENTITY_TO_SLUG)

config.py            ← Carrega e valida o .env
logger.py            ← Log estruturado em arquivo (chave:valor por linha)
```

**Lógica de retry:**
- Oracle: 1 retry após 5s em caso de timeout/falha de conexão
- API: 1 retry após 5s em falha de rede/5xx/429; **401** (chave inválida)
  interrompe o script imediatamente; erros de gravação (ex.: 422) num lote são
  reportados e o sync segue (exit 1)

**A escrita não usa mais `codi_ten`:** o tenant vem da `X-Api-Key`. A resolução
`erp_id → codi_tab` da tabela de preço agora é feita no servidor.

**Para adicionar ou alterar uma tabela**, edite `tables_spec.py` (colunas) e, se
for entidade nova, `ENTITY_TO_SLUG` em `api_writer.py` (o endpoint correspondente
precisa existir no AceleraX).

---

## Testes

```bash
# Rodar todos os testes
python -m pytest . --ignore=venv -v

# Rodar um arquivo específico
python -m pytest test_transformer.py -v

# Rodar um teste pelo nome
python -m pytest test_transformer.py::test_transform_renames_column_for_tabelas_preco -v
```

Os testes não precisam de Oracle nem da API no ar — todas as dependências externas (Oracle e o `requests`) são mockadas.

---

## Log

Formato de cada linha no `sync.log`:

```
[2026-05-31 14:23:45] TABLE:transac STATUS:start
[2026-05-31 14:23:46] TABLE:transac STATUS:success ROWS_FETCHED:1250 ROWS_UPSERTED:1250
[2026-05-31 14:24:10] LEVEL:ERROR TABLE:propried ERROR_TYPE:oracle_connection DETAIL:ORA-12170: timeout
[2026-05-31 14:25:00] LEVEL:SUMMARY EXIT_CODE:1
```
