-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  9. tabelas_preco — cabecalho  (era VTABELA_SINC + APEX_*)  ->  CABTAB
--
--     CODI_CIC NAO E EXTRAIDO DE PROPOSITO — nao aparece no SELECT abaixo.
--
--     Motivo: CABTAB.CODI_SAF e CABTAB.CICL_TAB estao vazios nas 3 tabelas, ou
--     seja, o ERP da AgroSanta NAO sabe a que safra cada tabela pertence. Esse
--     vinculo e decidido no APP (tela de Tabela de Preco) e o AceleraX o usa
--     para achar a tabela: ciclo -> codi_cic -> tabela (resolverTabelaCiclo).
--
--     Se a carga mandasse a coluna (mesmo como NULL), todo reprocessamento
--     APAGARIA o vinculo feito no app — foi exatamente o que aconteceu na
--     primeira rodada. Omitindo a coluna, o upsert nao a toca e o valor
--     definido no app SOBREVIVE as recargas.
--
--     Se um dia o ERP passar a preencher a safra, basta voltar a coluna aqui.
--
--     Colunas do motor (cambio/margens) ficam NULL: a AgroSanta nao tem o
--     APEX_GER_TABELAS. A POC roda sem motor de precificacao.
-- ---------------------------------------------------------------------
SELECT cb.TABE_CTA AS ID_CTAB,
       cb.DESC_CTA AS NOME,
       CASE cb.SITU_CTA WHEN 'A' THEN 'U' ELSE 'B' END AS SITU_CTAB,
       CAST(NULL AS NUMBER) AS PTAX_VISTA, CAST(NULL AS NUMBER) AS PTAX_PRAZO,
       CAST(NULL AS DATE)   AS DATA_BASE,  CAST(NULL AS DATE)   AS VENC_MAX,
       CAST(NULL AS NUMBER) AS CORREC_RS,  CAST(NULL AS NUMBER) AS CORREC_USS,
       CAST(NULL AS NUMBER) AS ANTEC_RS,   CAST(NULL AS NUMBER) AS ANTEC_USS,
       CAST(NULL AS NUMBER) AS MARGEM_FERT, CAST(NULL AS NUMBER) AS FAIXA_INI_FERT, CAST(NULL AS NUMBER) AS FAIXA_FIM_FERT,
       CAST(NULL AS NUMBER) AS MARGEM_DEF,  CAST(NULL AS NUMBER) AS FAIXA_INI_DEF,  CAST(NULL AS NUMBER) AS FAIXA_FIM_DEF,
       CAST(NULL AS NUMBER) AS MARGEM_ESP,  CAST(NULL AS NUMBER) AS FAIXA_INI_ESP,  CAST(NULL AS NUMBER) AS FAIXA_FIM_ESP,
       CAST(NULL AS NUMBER) AS MARGEM_SEM,  CAST(NULL AS NUMBER) AS FAIXA_INI_SEM,  CAST(NULL AS NUMBER) AS FAIXA_FIM_SEM
FROM CABTAB cb;
