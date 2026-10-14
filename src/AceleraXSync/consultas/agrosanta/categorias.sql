-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  2b. categorias_cliente  (NOVA)  ->  CATEGCLI
--     Mestre da categoria do parceiro (CLIENTE.CODI_CTC aponta pra ca).
--     Endpoint: /integracao/base/categorias. Rodar ANTES de transac.
--     A trigger tg_transac_categoria_erp (migracao 075) tambem cria a
--     categoria a partir de cada cliente, entao esta carga so garante nome
--     e existencia das categorias que ainda nao tem cliente vinculado.
-- ---------------------------------------------------------------------
SELECT c.CODI_CTC AS CODI_CCA,
       c.DESC_CTC AS NOME_CCA
FROM CATEGCLI c;
