-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- itens_tabela_preco  ->  TABELA.
--   CUSTO <- CUST_TAB (custo)            -> cust_itp
--   PRECO <- BASI_TAB (preco final, com a margem embutida) -> prrs_itp
--   Sao campos DIFERENTES — nao trocar um pelo outro. (Na AgroSanta:
--   BASI_TAB = CUST_TAB / (1 - MARG_TAB/100) em 1.363 de 1.395 itens.)
--   ATENCAO: o app RuralForte ainda nao aceita prrs_itp (a AgroSanta aceita, ver
--   integracao-base.ts dela); ate' la a coluna e' descartada pela API sem erro.
-- EMBALAGEM: fator so' quando o produto e' controlado por embalagem (CEST_PRO = 'B'):
--   QTE2_PRO se VMEM_PRO = 'S', senao QTE1_PRO; fora disso 1.
-- ORIGEM/ADICIONAL nao tem equivalente nativo -> fora do payload.
-- ESCOPO: grupos 49/50/71 fora (ver grupo.sql).
SELECT t.TABE_CTA AS ID_CTAB,
       t.CODI_PSV AS CODI_PSV,
       t.CUST_TAB AS CUSTO,
       t.BASI_TAB AS PRECO,
       CASE
         WHEN prd.CEST_PRO = 'B' AND prd.VMEM_PRO = 'S' THEN prd.QTE2_PRO
         WHEN prd.CEST_PRO = 'B'                        THEN prd.QTE1_PRO
         ELSE 1
       END          AS EMBALAGEM,
       fab.RAZA_TRA AS FABRICANTE,
       NVL(t.SITU_TAB, 'A') AS SITU_TAB
FROM TABELA t
INNER JOIN PRODSERV ps ON ps.CODI_PSV = t.CODI_PSV
INNER JOIN CABTAB cb   ON cb.TABE_CTA = t.TABE_CTA
LEFT JOIN PRODUTO prd  ON prd.CODI_PSV = t.CODI_PSV
LEFT JOIN TRANSAC fab  ON fab.CODI_TRA = prd.CODI_TRA
WHERE ps.CODI_GPR NOT IN (49, 50, 71);
