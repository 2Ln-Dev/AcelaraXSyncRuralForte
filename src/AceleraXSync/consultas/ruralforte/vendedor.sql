-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- vendedor  ->  VENDEDOR + PESSOAL. Na Rural o CODI_PES pode repetir em PESSOAL (HUB:
-- CatalogoPesquisas.cs), por isso o GROUP BY. CODI_SUP = supervisor de ordem 1 do
-- SUPERVISORVEND (a ordem 2 e' o analista, segundo o HUB).
-- NAO filtrar por filial: o CODI_EMP diz onde a pessoa esta cadastrada, nao onde atua.
SELECT v.CODI_PES,
       MIN(pes.NOME_PES) AS NOME_PES,
       MIN(pes.SITU_PES) AS SITU_PES,
       MIN(pes.CODI_EMP) AS CODI_EMP,
       MIN(s.CODS_PES)   AS CODI_SUP
FROM VENDEDOR v
INNER JOIN PESSOAL pes     ON pes.CODI_PES = v.CODI_PES
LEFT JOIN SUPERVISORVEND s ON s.CODI_PES = v.CODI_PES AND s.ORDE_SVV = 1
GROUP BY v.CODI_PES;
