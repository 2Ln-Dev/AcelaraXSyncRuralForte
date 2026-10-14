-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
-- 11. condicao  — INALTERADA, rodou sem ajuste
-- ---------------------------------------------------------------------
SELECT con.COND_CON, con.DESC_CON, ind.CODI_IND, ind.DESC_IND, con.STAT_CON
FROM CONDICAO con
LEFT JOIN INDEXADOR ind ON ind.CODI_IND = con.CODI_IND
WHERE con.TIPO_CON = 'R';
