# API de Integração — Gerar Tabela de Preço

Serviço HTTP (FastAPI) que gera **tabelas de preço no ERP** (SIAGRI/Oracle) a partir de uma
versão de precificação já liberada, e devolve os códigos das tabelas geradas.

É **idempotente**: chamando com os mesmos parâmetros, ele **não gera de novo** — devolve os
códigos que já existem. Toda chamada é registrada em log.

- Base URL (dev): `http://localhost:8100`
- **Sem autenticação** (uso em rede interna / VPN).
- Content-Type: `application/json`

---

## 1. Como subir o serviço

```bash
# a partir da pasta C:\Integracoes\acelera-x-sync
uvicorn gerar_tabela_api:app --host 0.0.0.0 --port 8100
```

Requisitos: `fastapi`, `uvicorn` e um driver Oracle (`cx_Oracle` ou `oracledb` + Oracle Instant
Client no PATH). Conexão ao banco vem do `.env` (`ORACLE_HOST`, `ORACLE_PORT`, `ORACLE_SID`
= service_name, `ORACLE_USER`, `ORACLE_PASSWORD`).

Documentação interativa (Swagger) sobe junto em: `http://localhost:8100/docs`

---

## 2. Endpoints

### `GET /health`
Verifica se o serviço está no ar.
```json
{ "status": "ok" }
```

### `POST /gerar-tabela`
Gera (ou reaproveita) a(s) tabela(s) de preço.

#### Corpo da requisição (JSON)

| Campo            | Tipo    | Obrigatório | Descrição |
|------------------|---------|-------------|-----------|
| `id_ger`         | int     | **sim**     | ID da versão de precificação **liberada** (`APEX_GER_TABELAS.ID`). |
| `condicao`       | string  | **sim**     | `"VISTA"` ou `"PRAZO"`. Define qual PTAX/correção usar. |
| `moeda_alvo`     | string  | **sim**     | `"R$"`, `"US$"` ou `"BARTER"`. |
| `commodity`      | int     | só p/ BARTER| `CODI_IND` do indexador (ex.: Soja = `1000002`). |
| `rt_reais`       | number  | só p/ BARTER| RT (relação de troca) em R$. Pode ser `0` (não gera a tabela de origem R$). |
| `rt_dolar`       | number  | só p/ BARTER| RT em US$. Pode ser `0` (não gera a de origem US$). Pelo menos uma RT > 0. |
| `cambio`         | number  | não         | Dólar/PTAX. Se omitido, usa o PTAX da condição na versão. |
| `id_modelo`      | int     | **sim**     | `TABE_CTA` de uma CABTAB **modelo** (copia empresa, indexador, markup, tipos… do cabeçalho). |
| `origem_chamada` | string  | não         | Identificação de quem chamou (vai pro log). |

> **BARTER**: informe `commodity` + pelo menos uma RT (`rt_reais` e/ou `rt_dolar`).
> **R$ / US$**: `commodity`/`rt_*` são ignorados.

#### Resposta (200)

```json
{
  "status": "GERADA",
  "reais": 2164,
  "dolar": 2165,
  "geradas": [
    { "origem": "R$",  "origem_cod": 1, "tabe_cta": 2164, "qtd_itens": 254, "descricao": "..." },
    { "origem": "US$", "origem_cod": 2, "tabe_cta": 2165, "qtd_itens": 406, "descricao": "..." }
  ]
}
```

| Campo         | Descrição |
|---------------|-----------|
| `status`      | `"GERADA"` (gerou agora) ou `"REAPROVEITADA"` (já existia, devolveu o existente). |
| `reais`       | Código (`TABE_CTA`) da tabela de **origem R$** (`origem_cod = 1`). `null` se não gerou essa origem. |
| `dolar`       | Código da tabela de **origem US$** (`origem_cod = 2`). `null` se não gerou. |
| `geradas`     | Lista detalhada por origem: `origem`, `origem_cod` (R$=1, US$=2), `tabe_cta`, `qtd_itens`, `descricao`. |

Uma geração produz **até 2 tabelas** (uma por origem do produto: R$ e US$).

#### Erros

`400 Bad Request` com o motivo em `detail`, por exemplo:
```json
{ "detail": "Versão ainda não está totalmente liberada." }
```
Outros motivos comuns: `"Selecione a commodity (indexador) para Barter."`,
`"Informe pelo menos uma RT (R$ ou US$) para Barter."`, `"CABTAB modelo X não encontrada."`,
`"Nenhum item com origem R$/US$ para gerar."`.

---

## 3. Idempotência

A chave de duplicidade é:

```
id_ger + moeda_alvo + commodity + condicao + rt_reais + rt_dolar
```

Se já existir uma geração com **exatamente** esses valores, a resposta vem com
`"status": "REAPROVEITADA"` e os **mesmos códigos** — nada é gerado de novo.
Mudou a RT (ou qualquer campo da chave) → é considerada nova e gera outra tabela.

---

## 4. Exemplos

### curl
```bash
curl -X POST http://localhost:8100/gerar-tabela \
  -H "Content-Type: application/json" \
  -d '{
    "id_ger": 487,
    "condicao": "PRAZO",
    "moeda_alvo": "BARTER",
    "commodity": 1000002,
    "rt_reais": 110,
    "rt_dolar": 19,
    "id_modelo": 2161,
    "origem_chamada": "acelerax"
  }'
```

### PowerShell
```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:8100/gerar-tabela `
  -ContentType application/json -Body '{
    "id_ger": 487, "condicao": "PRAZO", "moeda_alvo": "BARTER",
    "commodity": 1000002, "rt_reais": 110, "rt_dolar": 19, "id_modelo": 2161
  }'
```

### Tabela em R$ (sem barter)
```json
{ "id_ger": 487, "condicao": "PRAZO", "moeda_alvo": "R$", "id_modelo": 2161 }
```

---

## 5. Como descobrir os códigos de apoio

Estes valores hoje são consultados no Oracle (schema `AGROINSU`):

- **`id_ger`** — versões liberadas:
  `SELECT ID, NOVA_VERSAO, CODI_CIC FROM APEX_GER_TABELAS WHERE DATA_FINALIZACAO IS NOT NULL ORDER BY ID DESC;`
- **`commodity`** — indexadores ativos:
  `SELECT CODI_IND, DESC_IND FROM INDEXADOR WHERE SITU_IND = 'A' ORDER BY DESC_IND;`
  (Soja `1000002`, Milho `10000002`, Algodão `20000012`, …)
- **`id_modelo`** — tabelas modelo (CABTAB ativas):
  `SELECT TABE_CTA, DESC_CTA FROM CABTAB WHERE SITU_CTA = 'A' ORDER BY TABE_CTA DESC;`

> Se preferir, dá pra adicionar endpoints `GET /versoes`, `GET /commodities` e `GET /modelos`
> pra o consumo ficar self-service (sem tocar no banco). Peça ao time do ERP.

---

## 6. Log (auditoria)

Toda geração/reaproveitamento é registrado em `AGROINSU.APEX_GERTAB_LOG`
(uma linha por tabela gerada):

`DATA_HORA, ID_GER, CICLO, MOEDA_ALVO, COMMODITY, CONDICAO, ORIGEM, ORIGEM_COD,
RT_REAIS, RT_DOLAR, CAMBIO, TABE_CTA, QTD_ITENS, STATUS, ORIGEM_CHAMADA`.

---

## 7. Observações importantes

- **Escrita real no ERP**: a geração insere em `CABTAB` (cabeçalho) + `TABELA` (itens), grava
  histórico (`HISTORICOCABTAB`/`HISTORICOTABELA`) e, no Barter, a cotação da commodity em
  `INDVALOR` (para todas as empresas de `CADEMP`). Dispara os triggers de sincronização do ERP.
- **Ambiente**: o `.env` aponta pra base de **treino** durante testes
  (`ORACLE_HOST=10.0.0.234`, `ORACLE_SID=teste`). Para produção, ajustar o `.env`.
- **Preço (regra de negócio, resumo)**: `preço = custo ÷ (1 − margem − imposto)` (imposto de
  venda 0,925% embutido na margem); Barter converte pelo RT (`preço em sacas`); leva **todos os
  produtos** relacionados ao principal (via `VPRD_PRINCIPAL`).
