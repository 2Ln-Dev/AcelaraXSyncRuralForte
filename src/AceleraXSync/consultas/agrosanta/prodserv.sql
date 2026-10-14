-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  6. prodserv (produtos)  (era APEX_GRUPO)  ->  PRODSERV
--     O filtro original era por NOME de grupo do cliente antigo
--     ('01-DEFENSIVOS' etc). Aqui e lista NEGRA: tudo menos 50/51/52/53.
-- ---------------------------------------------------------------------
SELECT ps.CODI_PSV,
       ps.DESC_PSV,
       ps.UNID_PSV,
       ps.COMP_PSV,
       ps.CODI_GPR || '#' || ps.CODI_SBG AS CHAVEGRUPO,
       ps.SITU_PSV
FROM PRODSERV ps
WHERE ps.CODI_GPR NOT IN (50,51,52,53);
