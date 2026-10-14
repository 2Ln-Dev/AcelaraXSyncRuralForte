-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  5. grupo  (era APEX_GRUPO)  ->  GRUPO + SUBGRUPO
--     CHAVEGRUPO = '{CODI_GPR}#{CODI_SBG}' — tem que casar exatamente com
--     o chavegrupo montado no bloco 6 (prodserv), senao o vinculo
--     produto->grupo quebra e o motor nao resolve o segmento.
-- ---------------------------------------------------------------------
SELECT g.CODI_GPR || '#' || s.CODI_SBG AS CHAVEGRUPO,
       g.DESC_GPR,
       s.DESC_SBG
FROM GRUPO g
INNER JOIN SUBGRUPO s ON s.CODI_GPR = g.CODI_GPR
WHERE g.CODI_GPR NOT IN (50,51,52,53);
