-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  7. ciclos_safra  (era APEX_CICLO)  ->  CICLO
--     ANO_INI/ANO_FIM saem de DINI_CIC/DFIN_CIC (o original usava
--     DATA_VENC/DATA_VENC_FIM da APEX_CICLO, que nao existe aqui).
-- ---------------------------------------------------------------------
SELECT c.CODI_CIC,
       c.DESC_CIC                     AS DESC_CIC,
       EXTRACT(YEAR FROM c.DINI_CIC)  AS ANO_INI,
       EXTRACT(YEAR FROM c.DFIN_CIC)  AS ANO_FIM,
       c.SITU_CIC                     AS SITUACAO
FROM CICLO c;
