-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  2. vendedor  — INALTERADA, rodou sem ajuste
-- ---------------------------------------------------------------------
SELECT pes.CODI_PES,
       pes.NOME_PES,
       pes.SITU_PES,
       pes.CODI_EMP,
       s.CODS_PES AS CODI_SUP
FROM PESSOAL pes
INNER JOIN VENDEDOR v ON v.CODI_PES = pes.CODI_PES
LEFT JOIN SUPERVISORVEND s ON s.CODI_PES = v.CODI_PES AND s.ORDE_SVV = 1;
