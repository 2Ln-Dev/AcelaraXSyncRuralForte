-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- propried  ->  PROPRIED (so' de parceiros que sao cliente) + MUNICIPIO.
SELECT p.PROP_PRO,
       p.CODI_TRA,
       p.DESC_PRO,
       m.DESC_MUN,
       m.ESTA_MUN,
       NVL(p.SITU_PRO, 'A') AS SITU_PRO
FROM PROPRIED p
INNER JOIN CLIENTE cl ON cl.CODI_TRA = p.CODI_TRA
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = p.CODI_MUN;
