-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- empresa  ->  CADEMP. So' filiais ativas (SITU_EMP = 'A'), mesmo criterio do HUB.
-- ESCOPO (cliente, 08/10/2026): a filial 50 GERENCIAL fica fora. O mesmo filtro vai em
-- estoque e carteira. NAO vale para vendedor (CODI_EMP la' e' onde a pessoa esta
-- cadastrada, nao onde atua).
SELECT CODI_EMP, IDEN_EMP
FROM CADEMP
WHERE SITU_EMP = 'A'
  AND CODI_EMP <> 50;
