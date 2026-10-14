-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  4. propried  (era VDADOS_CLIENTE)  ->  CLIENTE
-- ---------------------------------------------------------------------
SELECT p.PROP_PRO,
       p.CODI_TRA,
       p.DESC_PRO,
       m.DESC_MUN,
       m.ESTA_MUN
FROM PROPRIED p
INNER JOIN CLIENTE cl ON cl.CODI_TRA = p.CODI_TRA
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = p.CODI_MUN;
