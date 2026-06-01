AceleraX Agro
Integração ERP → Supabase
Especificação para o desenvolvedor — Onda 1
Criação das views no schema HUBRAIZ e mapeamento De/Para das tabelas


Sumário

1. Objetivo e escopo
Este documento descreve o que deve ser entregue na primeira onda da integração entre o ERP (Senior Agribusiness, banco Oracle) e o banco PostgreSQL do AceleraX Agro (Supabase). O desenvolvedor responsável pela integração deve:
Criar as views de integração no usuário/schema HUBRAIZ (uma por entidade), lendo os dados de produção do ERP.
Garantir que cada view exponha exatamente as colunas descritas no De/Para de cada tabela.
Validar volumes e consistência usando as queries de validação ao final deste documento.

Tabelas desta onda: clientes (TRANSAC), propriedades (PROPRIED), ciclos/safra (APEX_CICLO), estoque (EST_CICLO_AF2), produtos (PRODUTO/PRODSERV), grupos e subgrupos (GRUPO/SUBGRUPO via APEX_GRUPO) e tabela de preço + itens (VTABELA_SINC).
A carga no Supabase (UPSERT) é feita pela API/rotina de sincronização do AceleraX. A responsabilidade do dev nesta etapa é entregar as views em HUBRAIZ no layout especificado.
2. Visão geral da arquitetura
O fluxo de dados segue três camadas. O ERP nunca é acessado diretamente pela aplicação: a integração lê apenas as views publicadas em HUBRAIZ.

AGROINSU — schema de produção do ERP. É a origem dos dados; as views fazem SELECT a partir dele.
HUBRAIZ — schema onde o dev cria as views de integração. É o ponto único de leitura da API.
Supabase — banco PostgreSQL do app. As tabelas de destino já existem; a API faz o UPSERT idempotente.
3. Conexão e credenciais

Segurança: estas credenciais são para o ambiente de integração. Não versionar em repositório; troque as senhas após o setup e prefira um cofre de segredos. O usuário hubraiz precisa de GRANT de SELECT nos objetos de AGROINSU usados pelas views.
4. Convenções obrigatórias

5. Tabelas da Onda 1 — De/Para Oracle → Supabase
Para cada entidade: (a) a view que o dev cria em HUBRAIZ, (b) a tabela de destino no Supabase, (c) o mapeamento coluna-a-coluna e (d) a chave usada no UPSERT.
5.1. Clientes — TRANSAC → transac
View em HUBRAIZ
De/Para → tabela transac
Chave de UPSERT: (codi_ten, codi_tra)
5.2. Propriedades — PROPRIED → propried
View em HUBRAIZ
De/Para → tabela propried
Chave de UPSERT: (codi_ten, prop_pro)  ·  Sincronizar transac antes de propried.
5.3. Ciclos / Safra — APEX_CICLO → ciclos_safra
APEX_CICLO é a tabela (nova, no Oracle) que define os ciclos com os nomes a serem apresentados no app. Ajuste os nomes das colunas abaixo aos reais de APEX_CICLO.
View em HUBRAIZ
De/Para → tabela ciclos_safra
Chave de UPSERT: (codi_ten, nome_csa) — codi_csa é UUID interno do app.
5.4. Estoque — EST_CICLO_AF2 → estoque
Query base fornecida (a view deve expor estas colunas):
Significado das colunas (origem)
De/Para → tabela estoque
Chave de UPSERT: (codi_ten, codi_pro, codi_fil, codi_csa)
5.5. Produtos — PRODUTO / PRODSERV → prodserv
O grupo enviado deve ser o grupo novo do De/Para (tabela APEX_GRUPO). Veja 5.6.
View em HUBRAIZ
De/Para → tabela prodserv
Chave de UPSERT: (codi_ten, codi_psv)  ·  Sincronizar grupo e subgrupo antes de prodserv.
5.6. Grupos e Subgrupos — GRUPO/SUBGRUPO via APEX_GRUPO
APEX_GRUPO faz o De/Para dos grupos: relaciona o grupo original do ERP (CODI_GPR) ao grupo novo usado no app. As views de grupo devem expor os grupos novos (distintos).
Views em HUBRAIZ
De/Para → tabela grupo
De/Para → tabela subgrupo
O vínculo grupo↔subgrupo não é modelado nessas tabelas; cada produto referencia seu próprio codi_gpr e codi_sbg em prodserv.
5.7. Tabela de Preço e Itens — VTABELA_SINC → tabelas_preco + itens_tabela_preco
VTABELA_SINC é uma view achatada (cabeçalho + item na mesma linha). A API a divide em duas tabelas: o cabeçalho (tabelas_preco, único por ID_CTAB) e os itens (itens_tabela_preco, um por CODI_PSV).
Query base fornecida
De/Para — cabeçalho → tabelas_preco (1 linha por ID_CTAB)
De/Para — itens → itens_tabela_preco (1 linha por CODI_PSV)
Chave de UPSERT: cabeçalho por (codi_ten, nome_tab); itens por (codi_tab, codi_pro).

6. Ordem obrigatória de sincronização
As tabelas mestre devem existir antes das dependentes para evitar registros órfãos:
7. Validação pós-sync
Rode no SQL Editor do Supabase após a primeira carga (troque '001' pelo codi_ten). Salvo a contagem, todas devem retornar 0 linhas.
7.1. Contagens por tenant
7.2. Propriedades órfãs (cliente inexistente) — deve dar 0
7.3. Estoque com produto inexistente — deve dar 0
7.4. Itens de tabela com produto inexistente — deve dar 0
7.5. Produtos com grupo inexistente — deve dar 0

8. Pendências e ajustes a confirmar
Pontos em que a origem (Oracle) e o destino atual (Supabase) ainda não casam 100%. Confirmar com o Lucas antes de codar a carga:

Fim do documento — AceleraX Agro · Integração ERP Onda 1