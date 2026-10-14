-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- prodserv (produtos)  ->  PRODSERV. CHAVEGRUPO igual ao de grupo.sql.
-- Produto sem grupo ou sem subgrupo vai com CHAVEGRUPO NULL (no TREINO eram 10, que
-- sairiam como '#' / '55#', chave que nao existe em grupo). Linha sem descricao
-- (cadastro lixo, ex.: 0002890) fica de fora.
-- ESCOPO: grupos 49/50/71 fora (ver grupo.sql). Produto SEM grupo tambem fica fora
-- (NOT IN com NULL) — sao cadastros antigos, quase todos inativos.
SELECT ps.CODI_PSV,
       ps.DESC_PSV,
       ps.UNID_PSV,
       ps.COMP_PSV,
       CASE WHEN ps.CODI_GPR IS NOT NULL AND ps.CODI_SBG IS NOT NULL
            THEN ps.CODI_GPR || '#' || ps.CODI_SBG END AS CHAVEGRUPO,
       ps.SITU_PSV
FROM PRODSERV ps
WHERE ps.DESC_PSV IS NOT NULL
  AND ps.CODI_GPR NOT IN (49, 50, 71);
