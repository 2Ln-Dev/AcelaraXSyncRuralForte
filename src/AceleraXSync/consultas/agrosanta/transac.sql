-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  3. transac (clientes)  (era VDADOS_CLIENTE)  ->  CLIENTE + CATEGCLI
--     INNER JOIN CLIENTE = so parceiros que sao cliente (mesmo efeito que
--     o INNER JOIN VDADOS_CLIENTE original).
--
--     CODI_PES (vendedor do cliente) vem de VENDEDORCLI, NAO de CLIENTE:
--     na AgroSanta CLIENTE.COD1_PES esta 100% vazio (0 de 21.553), enquanto
--     VENDEDORCLI e a carteira de verdade — 21.546 clientes, exatamente um
--     vendedor por cliente (zero duplicidade), 14 vendedores, todos presentes
--     na tabela VENDEDOR. E o que alimenta o escopo de dados por vendedor
--     (lib/services/escopo-dados.ts) e a hierarquia de aprovacao.
--
--     CODI_EMP = 2 e a filial ALTAMIRA (a unica ativa nesta base). Se a carga
--     passar a incluir outra filial, este filtro precisa acompanhar.
-- ---------------------------------------------------------------------
SELECT t.CODI_TRA,
       t.RAZA_TRA,
       m.DESC_MUN,
       m.ESTA_MUN,
       cat.DESC_CTC,
       cl.CODI_CTC,
       vc.COD1_PES AS CODI_PES
FROM TRANSAC t
INNER JOIN CLIENTE cl     ON cl.CODI_TRA = t.CODI_TRA
LEFT JOIN CATEGCLI cat    ON cat.CODI_CTC = cl.CODI_CTC
LEFT JOIN MUNICIPIO m     ON m.CODI_MUN = t.CODI_MUN
LEFT JOIN VENDEDORCLI vc  ON vc.CODI_TRA = t.CODI_TRA AND vc.CODI_EMP = 2;
