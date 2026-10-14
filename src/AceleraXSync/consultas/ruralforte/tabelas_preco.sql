-- Rural Forte (SIAGRI, Oracle 11.2, schema RURALF). RASCUNHO — validar no ERP (ver README.md).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.
--
-- tabelas_preco (cabecalho)  ->  CABTAB (o HUB ja le CABTAB.TABE_CTA/DESC_CTA).
--
-- CODI_CIC NAO e' extraido de proposito: se a carga mandar a coluna (mesmo NULL), toda
-- recarga apaga a safra vinculada a tabela na tela do app. Omitida, o upsert nao a toca.
-- Se o ERP da Rural souber a safra de cada tabela (CABTAB.CODI_SAF/CICL_TAB), avaliar.
--
-- Parametros do motor antigo (cambio, correcao, margens por segmento) nao existem no
-- SIAGRI nativo (eram do APEX_GER_TABELAS da Agroinsumos) e nao vao no payload.
SELECT cb.TABE_CTA AS ID_CTAB,
       cb.DESC_CTA AS NOME,
       CASE cb.SITU_CTA WHEN 'A' THEN 'U' ELSE 'B' END AS SITU_CTAB
FROM CABTAB cb;
