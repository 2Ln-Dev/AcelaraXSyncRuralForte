-- =====================================================================
-- Views da Onda "Motor de Pedido de Venda" (Caminho A).
-- Executar como AGROINSU (mesmo padrao de create_views.sql / fix_views_oracle.sql).
--
-- Origem dos campos novos: tabelas custom do appAgroinsumos no schema AGROINSU
-- (prefixo APEX_*), NAO a VTABELA_SINC/Siagri nativo.
--
-- PRE-REQUISITO no Supabase: a migration 018 (repo do app) precisa ter rodado,
-- criando as colunas de destino:
--   itens_tabela_preco: cust_itp, emba_itp, adic_itp, fabr_itp  (orig_itp ja existe)
--   tabelas_preco:      codi_csa, pxav/pxap/dbas/vmav/corr/coru/antr/antu
--                       + margens marg/inim/fimm por segmento (adu/qui/nut/sem)
-- Sem essas colunas, o UPSERT do PostgREST falha (coluna inexistente).
--
-- prodserv NAO e' alterada aqui: o vinculo grupo/segmento ja vem por chavegrupo
-- (tabela grupo ja existe no Supabase). V_AX_PRODUTOS permanece como em create_views.sql.
-- =====================================================================

-- ---------------------------------------------------------------------
-- ITENS DA TABELA DE PRECO  (insumos do motor)
-- Origem: APEX_ITAB_VIG (vigente), chave ID_CTAB + CODI_PSV.
-- fabr_itp: nome do fabricante via TRANSAC.RAZA_TRA (PRODUTO.CODI_TRA -> TRANSAC).
-- emba_itp: EMBALAGEM e' VARCHAR na origem e o destino e' numeric(10,3) —
--   convertido aqui (virgula->ponto, tolerante a lixo). Requer Oracle 12.2+
--   para "DEFAULT NULL ON CONVERSION ERROR".
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW V_AX_ITABELA_PRECO AS
SELECT i.ID_CTAB,
       i.CODI_PSV,
       i.CUSTO,
       TO_NUMBER(REPLACE(i.EMBALAGEM, ',', '.') DEFAULT NULL ON CONVERSION ERROR) AS EMBALAGEM,
       i.ORIGEM,
       i.ADICIONAL,
       ps.RAZA_TRA AS FABRICANTE
FROM APEX_ITAB_VIG i
LEFT JOIN (
    SELECT psv.CODI_PSV, RAZA_TRA AS RAZA_TRA
    FROM produto psv
    inner join transac tra on tra.codi_tra = psv.codi_tra

) ps ON ps.CODI_PSV = i.CODI_PSV
/

-- ---------------------------------------------------------------------
-- CABECALHO DA TABELA DE PRECO  (parametros de ciclo/cambio + margens)
-- Espinha: VTABELA_SINC (mantem NOME/CODI_CIC/SITU_CTAB ja validados).
-- Parametros + margens: APEX_GER_TABELAS, ligado por
--   ID_TAB_DEST = APEX_CTAB_VIG.ID_CTAB.
-- Margens cruas por grupo (4); o de/para 4 grupos -> segmentos do motor e' feito
--   em tables_spec.py. Corretivos nao e' usado no app (sem coluna aqui).
-- codi_csa NAO sai daqui: o sync resolve via CODI_CIC -> ciclos_safra (fk_resolve).
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW V_AX_TABELA_PRECO AS
SELECT DISTINCT
       v.ID_CTAB,
       v.NOME,
       v.CODI_CIC,
       CASE WHEN v.SITU_CTAB = 'A' THEN 'U'
            WHEN v.SITU_CTAB = 'I' THEN 'B'
            WHEN v.SITU_CTAB = 'P' THEN 'B'
       END AS SITU_CTAB,
       g.PTAX_VISTA  AS PTAX_VISTA,
       g.PTAX_PRAZO  AS PTAX_PRAZO,
       g.DATA_BASE   AS DATA_BASE,
       g.VENC_MAX    AS VENC_MAX,
       g.CORREC_RS   AS CORREC_RS,
       g.CORREC_USS  AS CORREC_USS,
       g.ANTEC_RS    AS ANTEC_RS,
       g.ANTEC_USS   AS ANTEC_USS,
       MARGEM_FERT , FAIXA_INI_FERT , FAIXA_FIM_FERT,
MARGEM_DEF , FAIXA_INI_DEF , FAIXA_FIM_DEF,
MARGEM_ESP , FAIXA_INI_ESP , FAIXA_FIM_ESP,
MARGEM_SEM , FAIXA_INI_SEM , FAIXA_FIM_SEM
FROM VTABELA_SINC v
LEFT JOIN APEX_CTAB_VIG   c ON c.ID_CTAB     = v.ID_CTAB
LEFT JOIN APEX_GER_TABELAS g ON g.ID_TAB_DEST = c.ID_CTAB
/

GRANT SELECT ON V_AX_ITABELA_PRECO TO HUBRAIZ
/
GRANT SELECT ON V_AX_TABELA_PRECO TO HUBRAIZ
/

EXIT
