-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- ciclos_safra  ->  CICLO. Anos a partir de DINI_CIC/DFIN_CIC (como na AgroSanta).
SELECT c.CODI_CIC,
       c.DESC_CIC                    AS DESC_CIC,
       EXTRACT(YEAR FROM c.DINI_CIC) AS ANO_INI,
       EXTRACT(YEAR FROM c.DFIN_CIC) AS ANO_FIM,
       c.SITU_CIC                    AS SITUACAO
FROM CICLO c;
