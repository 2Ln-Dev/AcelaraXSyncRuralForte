-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
-- 10. itens_tabela_preco  (era APEX_ITAB_VIG)  ->  TABELA
--     CUSTO <- CUST_TAB (custo real, 100% preenchido) -> cust_itp.
--     PRECO <- BASI_TAB -> prrs_itp. BASI_TAB e o PRECO FINAL, com a margem ja
--     embutida: confirmado que BASI_TAB = CUST_TAB / (1 - MARG_TAB/100) em
--     1.363 de 1.395 itens. Sao campos DIFERENTES e cada um vai pro seu destino
--     — nao trocar um pelo outro.
--     EMBALAGEM vem de PRODUTO (nao da tabela de preco): o fator de embalagem
--     so existe quando o produto e controlado por embalagem (CEST_PRO='B');
--     nesse caso usa QTE2_PRO se VMEM_PRO='S', senao QTE1_PRO. Fora disso e 1
--     (produto vendido na unidade). Destino emba_itp = numeric(10,3), aceita
--     nulo — ~19 produtos ficam sem valor por QTE1/QTE2 nulos.
--     ORIGEM/ADICIONAL nao tem equivalente nativo -> NULL.
-- ---------------------------------------------------------------------
SELECT t.TABE_CTA AS ID_CTAB,
       t.CODI_PSV AS CODI_PSV,
       t.CUST_TAB AS CUSTO,
       t.BASI_TAB AS PRECO,
       CASE
         WHEN prd.CEST_PRO = 'B' AND prd.VMEM_PRO = 'S' THEN prd.QTE2_PRO
         WHEN prd.CEST_PRO = 'B'                        THEN prd.QTE1_PRO
         ELSE 1
       END                         AS EMBALAGEM,
       CAST(NULL AS VARCHAR2(20))  AS ORIGEM,
       CAST(NULL AS NUMBER)        AS ADICIONAL,
       fab.RAZA_TRA                AS FABRICANTE
FROM TABELA t
INNER JOIN PRODSERV ps ON ps.CODI_PSV = t.CODI_PSV
LEFT JOIN PRODUTO prd  ON prd.CODI_PSV = t.CODI_PSV
LEFT JOIN (
  SELECT pr.CODI_PSV, tr.RAZA_TRA
  FROM PRODUTO pr
  INNER JOIN TRANSAC tr ON tr.CODI_TRA = pr.CODI_TRA
) fab ON fab.CODI_PSV = t.CODI_PSV
WHERE ps.CODI_GPR NOT IN (50,51,52,53);
