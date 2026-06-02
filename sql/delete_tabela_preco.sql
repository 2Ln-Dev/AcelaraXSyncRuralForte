-- =====================================================================
-- Limpa SOMENTE a tabela de preco (cabecalho + itens) para recarga limpa.
-- Apaga os itens ANTES do cabecalho (FK itens.codi_tab -> tabelas_preco.codi_tab).
-- Rodar no SQL Editor do Supabase antes da atualizacao do sync.
-- =====================================================================

BEGIN;
DELETE FROM public.itens_tabela_preco;
DELETE FROM public.tabelas_preco;
COMMIT;

-- Conferencia (deve dar 0 nas duas):
-- select 'tabelas_preco' t, count(*) from public.tabelas_preco
-- union all select 'itens_tabela_preco', count(*) from public.itens_tabela_preco;
