-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- empresa  ->  CADEMP. So' filiais ativas (SITU_EMP = 'A'), mesmo criterio do HUB.
-- ESCOPO (cliente): TODAS as filiais ativas entram, inclusive a 50 (GERENCIAL).
-- O recorte de escopo e' por GRUPO de produto (49/50/71), nao por filial — ver grupo.sql.
SELECT CODI_EMP, IDEN_EMP
FROM CADEMP
WHERE SITU_EMP = 'A';
