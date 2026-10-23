-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- empresa  ->  CADEMP. So' filiais ativas (SITU_EMP = 'A'), mesmo criterio do HUB.
-- ESCOPO (cliente): TODAS as filiais ativas entram, inclusive a 50 (GERENCIAL).
-- O recorte de escopo e' por GRUPO de produto (49/50/71), nao por filial — ver grupo.sql.
--
-- CFOP do pedido (migracao 101 do app): UF da filial (MUNICIPIO) + CFOP padrao dela
-- dentro do estado (PARAMGER.CFOP_PAR) e fora (PARAMGERTRANSAC.CFOP_PRT). E' o que a
-- tela do pedido usa: a FABRICA (2) vende 5101/6101, as lojas 5102/6102.
SELECT e.CODI_EMP,
       e.IDEN_EMP,
       m.ESTA_MUN,
       pg.CFOP_PAR,
       pt.CFOP_PRT
FROM CADEMP e
LEFT JOIN MUNICIPIO m ON m.CODI_MUN = e.CODI_MUN
LEFT JOIN PARAMGER pg ON pg.CODI_EMP = e.CODI_EMP
LEFT JOIN PARAMGERTRANSAC pt ON pt.CODI_EMP = e.CODI_EMP
WHERE e.SITU_EMP = 'A';
