-- =====================================================================
-- Migracao de schema p/ a nova carga inicial (idempotente).
-- Rodar no SQL Editor do Supabase DEPOIS do wipe (delete_integracao.sql),
-- com as tabelas da integracao ja vazias.
-- O ON CONFLICT do PostgREST aceita indice/constraint unico nas colunas alvo.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 0) Remover FKs dependentes ANTES de recriar as constraints-alvo.
--    (Idempotencia: numa reexecucao, uma constraint-alvo nao pode ser
--    dropada enquanto existir um FK que dependa dela.)
-- ---------------------------------------------------------------------
ALTER TABLE public.metas         DROP CONSTRAINT IF EXISTS metas_codi_ten_codi_gpr_fkey;
ALTER TABLE public.metas         DROP CONSTRAINT IF EXISTS metas_chavegrupo_fkey;
ALTER TABLE public.tabelas_preco DROP CONSTRAINT IF EXISTS tabelas_preco_ciclo_fkey;

-- ---------------------------------------------------------------------
-- 1) Constraints UNIQUE por codigo do ERP (suportam o UPSERT)
-- ---------------------------------------------------------------------
-- ciclos_safra: UNIQUE como CONSTRAINT (suporta UPSERT e serve de alvo do FK da tabela de preco)
DROP INDEX IF EXISTS public.ux_ciclos_safra_erp;
ALTER TABLE public.ciclos_safra DROP CONSTRAINT IF EXISTS uq_ciclos_safra_erp;
ALTER TABLE public.ciclos_safra ADD  CONSTRAINT uq_ciclos_safra_erp UNIQUE (codi_ten, codi_cic);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tabelas_preco_erp
  ON public.tabelas_preco (codi_ten, erp_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_itens_tabela_preco_erp
  ON public.itens_tabela_preco (codi_ten, erp_id, codi_pro);

CREATE UNIQUE INDEX IF NOT EXISTS ux_estoque_erp
  ON public.estoque (codi_ten, codi_pro, codi_fil);

-- ---------------------------------------------------------------------
-- 2) grupo: novo modelo por CHAVEGRUPO (grupo + subgrupo numa linha).
--    A view V_AX_GRUPOS nao fornece mais codi_gpr; logo codi_gpr sai da
--    PK e vira opcional, e a chave passa a ser (codi_ten, chavegrupo).
--    (O FK antigo da metas ja foi removido na secao 0.)
-- ---------------------------------------------------------------------
ALTER TABLE public.grupo ADD COLUMN IF NOT EXISTS chavegrupo text;
ALTER TABLE public.grupo ADD COLUMN IF NOT EXISTS desc_sbg   text;

-- dropar a PK antiga (codi_ten, codi_gpr), qualquer que seja o nome
DO $$
DECLARE pk text;
BEGIN
  SELECT conname INTO pk FROM pg_constraint
   WHERE conrelid = 'public.grupo'::regclass AND contype = 'p';
  IF pk IS NOT NULL THEN
    EXECUTE format('ALTER TABLE public.grupo DROP CONSTRAINT %I', pk);
  END IF;
END $$;

ALTER TABLE public.grupo ALTER COLUMN codi_gpr DROP NOT NULL;

-- nova chave unica (serve para o UPSERT e como alvo do FK da metas)
ALTER TABLE public.grupo DROP CONSTRAINT IF EXISTS ux_grupo_chavegrupo;
ALTER TABLE public.grupo ADD  CONSTRAINT ux_grupo_chavegrupo UNIQUE (codi_ten, chavegrupo);

-- ---------------------------------------------------------------------
-- 3) metas: passa a referenciar o novo grupo por chavegrupo.
--    chavegrupo e DERIVADA de codi_gpr#codi_sbg (coluna gerada) -> nunca
--    inserir/atualizar manualmente; o app continua gravando codi_gpr/codi_sbg.
-- ---------------------------------------------------------------------
ALTER TABLE public.metas ADD COLUMN IF NOT EXISTS chavegrupo text
  GENERATED ALWAYS AS (codi_gpr || '#' || codi_sbg) STORED;

ALTER TABLE public.metas DROP CONSTRAINT IF EXISTS metas_chavegrupo_fkey;
ALTER TABLE public.metas ADD  CONSTRAINT metas_chavegrupo_fkey
  FOREIGN KEY (codi_ten, chavegrupo) REFERENCES public.grupo (codi_ten, chavegrupo);

-- ---------------------------------------------------------------------
-- 4) prodserv: vinculo ao novo grupo (= CODI_GPR#CODI_SBG) e status.
-- ---------------------------------------------------------------------
ALTER TABLE public.prodserv ADD COLUMN IF NOT EXISTS chavegrupo text;
ALTER TABLE public.prodserv ADD COLUMN IF NOT EXISTS situ_psv   text;

-- ---------------------------------------------------------------------
-- 5) transac: categoria de cliente (uso futuro pelo app).
-- ---------------------------------------------------------------------
ALTER TABLE public.transac ADD COLUMN IF NOT EXISTS desc_ctc text;
ALTER TABLE public.transac ADD COLUMN IF NOT EXISTS codi_ctc text;

-- ---------------------------------------------------------------------
-- 6) tabelas_preco: ciclo/safra da tabela de preco + FK para ciclos_safra.
-- ---------------------------------------------------------------------
ALTER TABLE public.tabelas_preco ADD COLUMN IF NOT EXISTS codi_cic bigint;  -- mesmo tipo de ciclos_safra.codi_cic
ALTER TABLE public.tabelas_preco DROP CONSTRAINT IF EXISTS tabelas_preco_ciclo_fkey;
ALTER TABLE public.tabelas_preco ADD  CONSTRAINT tabelas_preco_ciclo_fkey
  FOREIGN KEY (codi_ten, codi_cic) REFERENCES public.ciclos_safra (codi_ten, codi_cic);

-- ---------------------------------------------------------------------
-- 7) itens_tabela_preco: moeda/origem do preco (ORIGEM = Reais/Dolar).
-- ---------------------------------------------------------------------
ALTER TABLE public.itens_tabela_preco ADD COLUMN IF NOT EXISTS orig_itp text;

-- ---------------------------------------------------------------------
-- (Opcional/recomendado) Remover as UNIQUE antigas por NOME de
-- ciclos_safra/tabelas_preco. Inspecione e remova se existirem:
--   select conname, pg_get_constraintdef(oid)
--   from pg_constraint
--   where conrelid in ('public.ciclos_safra'::regclass,
--                      'public.tabelas_preco'::regclass)
--     and contype = 'u';
--   ALTER TABLE public.ciclos_safra  DROP CONSTRAINT <constraint_por_nome_csa>;
--   ALTER TABLE public.tabelas_preco DROP CONSTRAINT <constraint_por_nome_tab>;
-- ---------------------------------------------------------------------
