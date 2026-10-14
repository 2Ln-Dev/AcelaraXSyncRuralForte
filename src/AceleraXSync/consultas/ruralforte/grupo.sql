-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- grupo  ->  GRUPO + SUBGRUPO. CHAVEGRUPO = '{CODI_GPR}#{CODI_SBG}' — tem que casar
-- exatamente com o CHAVEGRUPO de prodserv.sql.
-- ATENCAO: na Rural APEX_GRUPO e' grupo de USUARIO do HUB, nao grupo de produto.
-- ESCOPO (cliente, 08/10/2026): lista NEGRA de grupos — CODI_GPR NOT IN (49,50,71):
--   49 MATERIAL CONSUMO (uso e consumo), 50 IMOBILIZADO, 71 SERVICOS.
-- O mesmo filtro vai em prodserv, estoque, itens_tabela_preco e carteira.
SELECT g.CODI_GPR || '#' || s.CODI_SBG AS CHAVEGRUPO,
       g.DESC_GPR,
       s.DESC_SBG
FROM GRUPO g
INNER JOIN SUBGRUPO s ON s.CODI_GPR = g.CODI_GPR
WHERE g.CODI_GPR NOT IN (49, 50, 71);
