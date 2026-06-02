-- =====================================================================
-- WIPE TOTAL dos dados da integracao (TODOS os tenants).
-- PASSO 1 da carga inicial: rodar ESTE script ANTES do fix_unique_keys.sql
-- (constraints/indices unicos sao criados depois, nas tabelas ja vazias).
-- IRREVERSIVEL. Apaga na ordem inversa de FK (filhas primeiro).
-- =====================================================================

BEGIN;

DELETE FROM public.itens_tabela_preco;
DELETE FROM public.tabelas_preco;
DELETE FROM public.estoque;
DELETE FROM public.ciclos_safra;
DELETE FROM public.prodserv;
DELETE FROM public.subgrupo;
DELETE FROM public.grupo;
DELETE FROM public.propried;
DELETE FROM public.transac;

COMMIT;

-- Conferencia (deve retornar 0 em todas):
-- select 'transac' t, count(*) from public.transac
-- union all select 'propried', count(*) from public.propried
-- union all select 'grupo', count(*) from public.grupo
-- union all select 'subgrupo', count(*) from public.subgrupo
-- union all select 'prodserv', count(*) from public.prodserv
-- union all select 'ciclos_safra', count(*) from public.ciclos_safra
-- union all select 'estoque', count(*) from public.estoque
-- union all select 'tabelas_preco', count(*) from public.tabelas_preco
-- union all select 'itens_tabela_preco', count(*) from public.itens_tabela_preco;
