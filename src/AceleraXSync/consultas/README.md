# Consultas de extração por cliente

Cada pasta é um cliente; cada arquivo `<entidade>.sql` é **um SELECT** que o BaseSync roda
no Oracle quando `Sync:ConsultasDir` tem o nome do cliente (ex.: `"ruralforte"`). Os `.sql`
vão **embutidos no exe** (EmbeddedResource): não existem em disco no servidor do cliente —
alterar uma consulta exige novo build.
Sem `ConsultasDir`, o sync lê as views `V_AX_*` do cliente original (Agroinsumos).

## Regras

- **Nome do arquivo = nome da entidade** em `Sync/TablesSpec.cs`: `empresa`, `vendedor`,
  `categorias`, `transac`, `propried`, `grupo`, `prodserv`, `ciclos_safra`, `estoque`,
  `tabelas_preco`, `itens_tabela_preco`, `condicao`, `condicoes_categoria`,
  `vcto_fixo_safra`, `vcto_fixo_mensal` (vencimento fixo da condição), `carteira`.
- **Sem arquivo = entidade fora da carga** desse cliente (o log mostra `status=pulada`).
  Arquivo com nome que não é entidade gera aviso no início.
- **Os apelidos das colunas são o contrato** (coluna Oracle do De/Para em `TablesSpec.cs`).
  Coluna do resultado que não está no contrato é ignorada (com aviso).
- **Coluna do contrato que a consulta não traz não vai no payload**, então o upsert não
  mexe nela no app. É assim que se preserva um valor mantido na tela, por exemplo o
  `CODI_CIC` da tabela de preço. Mandar `NULL` explícito **apaga** o valor.
- Tabelas sem schema: a conexão aplica `ALTER SESSION SET CURRENT_SCHEMA = <Oracle:Schema>`.
- O `;` final é opcional; o arquivo roda igual no SQL Developer e no sync.
- A tabela de preço precisa de `tabelas_preco.sql`; sem `itens_tabela_preco.sql` vão só os
  cabeçalhos.

## Clientes

| Pasta | ERP | Situação |
|---|---|---|
| `agrosanta` | SIAGRI nativo, Oracle 19c, schema `ALTAMIRA` | Validada: carga inicial feita com estas consultas (origem `AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql`). |
| `ruralforte` | SIAGRI + HUB2LN, Oracle **11.2**, schema `RURALF` | Validada no schema `TREINO` em 08/10/2026 (DryRun completo). Faltam as decisões de escopo abaixo. |

## Rural Forte

### Validação no TREINO (08/10/2026, DryRun, modo ponte)

| Entidade | Linhas | Tempo |
|---|---:|---:|
| empresa | 4 (1 LOJA, 2 FABRICA, 3 RURAL LOJA, 50 GERENCIAL) | 2 s |
| vendedor | 62 (20 ativos; 10 com supervisor) | 1 s |
| transac | 3.995 (2.493 com vendedor; todos com categoria) | 2 s |
| propried | 3.631 | 2 s |
| grupo | 149 (45 grupos) | 1 s |
| prodserv | 8.660 (2.695 ativos) | 2 s |
| ciclos_safra | 129 (6 ativos) | 1 s |
| condicao | 73 | 1 s |
| tabelas_preco + itens | 16 tabelas + 15.983 itens | 2 s |
| carteira | 167 itens em aberto | 54 s |
| estoque | 9.625 (≈ 2.400 produtos × 4 filiais; 2.564 com saldo físico, 95 negativos) | 9,4 min |

Conferido nos dumps:
- nenhuma chave duplicada;
- nenhum vínculo órfão (cliente → vendedor, propriedade → cliente, item → tabela,
  carteira → cliente/produto/vendedor/propriedade);
- nenhum texto maior que a coluna do app.

Correção feita durante a validação: produto sem subgrupo saía com `chavegrupo` `#`/`55#`.
Agora sai `NULL`, e a linha sem descrição foi filtrada.

Dados que vêm vazios do ERP, e por isso são aceitos vazios no app:
- carteira: safra em 150 de 167 itens e propriedade em 22;
- clientes: vendedor em 1.502 (cliente sem título a receber — regra antiga). Desde 15/10/2026
  o vendedor vem só de `VENDEDORCLI.COD1_PES`: em RURALF, 983 de 4.112 clientes têm vendedor;
  cliente fora da carteira fica sem vendedor (decisão do cliente);
- itens: embalagem em 260 e fabricante em 64.

### Escopo definido pelo cliente

- **Todas as filiais ativas entram, inclusive a 50 (GERENCIAL).** O recorte é por GRUPO
  de produto, não por filial. (Correção 13/10/2026: antes a 50 estava fora por engano —
  o "50" que fica de fora é o GRUPO 50 IMOBILIZADO, não a filial 50.)
- **Grupos fora: `CODI_GPR NOT IN (49, 50, 71)`**, ou seja, 49 MATERIAL CONSUMO (uso e
  consumo), 50 IMOBILIZADO e 71 SERVICOS. O filtro vai em `grupo`, `prodserv`, `estoque`,
  `itens_tabela_preco` e `carteira`. Produto sem grupo também fica fora.

### Ainda em aberto

- **Tabelas de preço**: vão as 16, com 4 vigentes (U) e 12 bloqueadas (B), incluindo as
  "LIMITADA <vendedor>" e as "CÓPIA DE TRANSFERENCIAS".

### Antes da carga real

1. App RuralForte: rodar `sql/077_contrato_integracao_base.sql` no Supabase. Sem ela, os
   upserts de empresa, grupo, ciclos e tabela de preço falham.
2. Cadastrar a API key em `integracao_api_keys` do RuralForte, para o tenant da Rural.
3. Preencher `Acelera:ApiUrl`/`ApiKey` no `appsettings.Development.json` (ou
   `.Production.json`), que é gitignored, e passar `Sync:DryRun` para `false`.
4. Rodar uma entidade por vez na primeira vez: `--Sync:Somente=empresa`, depois `vendedor`,
   e assim por diante, na ordem do `SyncOrder`.

### Ainda não vai na carga

- **`categorias.sql` não existe de propósito.** O app RuralForte ainda não tem
  `POST /integracao/base/categorias` (a AgroSanta tem). As categorias chegam assim mesmo
  pelo `codi_ctc`/`desc_ctc` do cliente.
- **`PRECO` → `prrs_itp`**: o app RuralForte descarta a coluna em silêncio, porque ela não
  está na lista de colunas aceitas. O preço da tabela só chega depois de portar essa
  mudança da AgroSanta (`integracao-base.ts` e o motor de preço).
