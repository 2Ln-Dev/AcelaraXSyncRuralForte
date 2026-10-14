-- AgroSanta (Senior/SIAGRI nativo, Oracle 19c, schema ALTAMIRA).
-- Origem: AcelaraX-AgroSanta/sql/carga-inicial/02_extracao_agrosanta.sql (validado la).
-- Os APELIDOS das colunas sao o contrato (TablesSpec.cs). Um SELECT por arquivo.

-- ---------------------------------------------------------------------
--  1. empresa  (era V_AX_EMPRESA / APEX_CADEMP)  ->  CADEMP
--     SO A LOJA 2 (ALTAMIRA). A filial 1 existe no cadastro mas esta INATIVA
--     (SITU_EMP='I') e nao faz parte da operacao da AgroSanta.
-- ---------------------------------------------------------------------
SELECT CODI_EMP, IDEN_EMP
FROM CADEMP
WHERE CODI_EMP = 2;
