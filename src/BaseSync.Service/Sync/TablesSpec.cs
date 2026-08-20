namespace BaseSync.Service.Sync;

/// <summary>
/// Contrato de uma entidade da carga de base: view de origem, slug do endpoint e o
/// De/Para Oracle -> coluna do banco do AceleraX.
///
/// PORTE FIEL de tables_spec.py (repo raiz). A ORDEM das colunas e' preservada porque
/// o SELECT e' montado a partir dela, igual ao oracle_reader.py.
/// </summary>
/// <param name="OracleView">View lida no Oracle (ex.: AGROINSU.V_AX_EMPRESA).</param>
/// <param name="Slug">Slug do endpoint POST /api/v1/integracao/base/&lt;slug&gt;.</param>
/// <param name="Columns">De/Para: coluna Oracle (chave) -> coluna do destino (valor).</param>
public sealed record EntidadeSpec(
    string OracleView,
    string? Slug,
    IReadOnlyList<KeyValuePair<string, string>> Columns);

/// <summary>
/// Espelho do tables_spec.py + ENTITY_TO_SLUG do api_writer.py. Fonte unica da verdade
/// do que e' lido e de como e' renomeado. NAO alterar sem alterar o Python (o contrato
/// funcional e' compartilhado; o Python segue sendo a referencia de validacao).
/// </summary>
public static class TablesSpec
{
    // Entidades da tabela de preco: vao juntas num endpoint combinado (ver ApiClient).
    public const string PrecoHeader = "tabelas_preco";
    public const string PrecoItens = "itens_tabela_preco";
    public const string SlugTabelasPreco = "tabelas-preco";

    private static KeyValuePair<string, string> C(string oracle, string destino) => new(oracle, destino);

    /// <summary>SYNC_ORDER do tables_spec.py — a ordem respeita as dependencias
    /// (empresa primeiro; ciclos antes da tabela de preco; header antes dos itens).</summary>
    public static readonly IReadOnlyList<string> SyncOrder = new[]
    {
        "empresa",
        "vendedor",
        "transac",
        "propried",
        "grupo",
        "prodserv",
        "ciclos_safra",
        "estoque",
        "tabelas_preco",
        "itens_tabela_preco",
        "condicao",
        "carteira",
    };

    public static readonly IReadOnlyDictionary<string, EntidadeSpec> Entidades =
        new Dictionary<string, EntidadeSpec>(StringComparer.Ordinal)
        {
            ["empresa"] = new("AGROINSU.V_AX_EMPRESA", "empresas", new[]
            {
                // APEX_CADEMP so' tem 2 colunas: CODI_EMP (codigo da filial) e
                // IDEN_EMP (identificacao/nome, ex.: '01-SRR').
                C("CODI_EMP", "codi_emp"),
                C("IDEN_EMP", "iden_emp"),
            }),

            ["vendedor"] = new("AGROINSU.V_AX_VENDEDOR", "vendedores", new[]
            {
                C("CODI_PES", "codi_pes"),
                C("NOME_PES", "nome_pes"),
                C("SITU_PES", "situ_pes"),
                C("CODI_EMP", "codi_emp"),
            }),

            ["transac"] = new("AGROINSU.V_AX_CLIENTES", "clientes", new[]
            {
                C("CODI_TRA", "codi_tra"),
                C("RAZA_TRA", "raza_tra"),
                C("DESC_MUN", "desc_mun"),
                C("ESTA_MUN", "esta_mun"),
                C("CODI_PES", "codi_pes"),
                C("DESC_CTC", "desc_ctc"),
                C("CODI_CTC", "codi_ctc"),
            }),

            ["propried"] = new("AGROINSU.V_AX_PROPRIEDADES", "propriedades", new[]
            {
                C("PROP_PRO", "prop_pro"),
                C("CODI_TRA", "codi_tra"),
                C("DESC_PRO", "desc_pro"),
                C("DESC_MUN", "desc_mun"),
                C("ESTA_MUN", "esta_mun"),
            }),

            ["grupo"] = new("AGROINSU.V_AX_GRUPOS", "grupos", new[]
            {
                C("CHAVEGRUPO", "chavegrupo"),
                C("DESC_GPR", "desc_gpr"),
                C("DESC_SBG", "desc_sbg"),
            }),

            ["prodserv"] = new("AGROINSU.V_AX_PRODUTOS", "produtos", new[]
            {
                C("CODI_PSV", "codi_psv"),
                C("DESC_PSV", "desc_psv"),
                C("UNID_PSV", "unid_psv"),
                C("COMP_PSV", "comp_psv"),
                // O vinculo grupo/segmento se faz por chavegrupo (tabela grupo ja existe
                // no Supabase). O motor resolve o segmento a partir de chavegrupo/grupo,
                // entao o sync NAO precisa mandar codi_gpr.
                C("CHAVEGRUPO", "chavegrupo"),
                C("SITU_PSV", "situ_psv"),
            }),

            ["ciclos_safra"] = new("AGROINSU.V_AX_CICLOS", "ciclos", new[]
            {
                C("CODI_CIC", "codi_cic"),
                C("DESC_CIC", "nome_csa"),
                C("ANO_INI", "anoi_csa"),
                C("ANO_FIM", "anof_csa"),
                C("SITUACAO", "stat_csa"),
            }),

            ["estoque"] = new("AGROINSU.V_AX_ESTOQUE", "estoque", new[]
            {
                C("CODI_EMP", "codi_fil"),
                C("CODI_PSV", "codi_pro"),
                C("DESC_PSV", "desc_pro"),
                C("QTE1_PRO", "qtd1_est"),
                C("QTE2_PRO", "qtd2_est"),
                C("VALOR_UNIT", "vlun_est"),
                C("EF", "esfi_est"),
                C("RDE", "rese_est"),
                C("PCNR", "pcnr_est"),
                C("PVNE", "pvne_est"),
                C("CF", "cofu_est"),
                C("VF", "vefu_est"),
                C("MT", "mtra_est"),
                C("DISPONIVEL", "esdi_est"),
            }),

            [PrecoHeader] = new("AGROINSU.V_AX_TABELA_PRECO", null, new[]
            {
                C("ID_CTAB", "erp_id"),
                C("NOME", "nome_tab"),
                // Vinculo com a safra e' por codi_cic (chave natural do ERP). O sync NAO
                // resolve codi_csa.
                C("CODI_CIC", "codi_cic"),
                C("SITU_CTAB", "stat_tab"),
                // Parametros de ciclo/cambio (APEX_GER_TABELAS) — etapas 1-4 do motor.
                C("PTAX_VISTA", "pxav_tab"),
                C("PTAX_PRAZO", "pxap_tab"),
                C("DATA_BASE", "dbas_tab"),
                C("VENC_MAX", "vmav_tab"),
                C("CORREC_RS", "corr_tab"),
                C("CORREC_USS", "coru_tab"),
                C("ANTEC_RS", "antr_tab"),
                C("ANTEC_USS", "antu_tab"),
                // Margens por segmento (etapas 5 e 8 do motor) — de/para 4 grupos do
                // ERP -> 4 dos 5 segmentos do motor:
                //   FERT (Fertilizantes) -> Adubos (adu)
                //   DEF  (Defensivos)    -> Quimico (qui)
                //   ESP  (Especialidades)-> Nutricao/Micro (nut)
                //   SEM  (Sementes)      -> Semente (sem)
                // Corretivos (cor) NAO e' usado no app -> marg/inim/fimm_cor ficam NULL.
                // Este de/para precisa ser consistente com lib/motor/segmentos.ts (app).
                C("MARGEM_FERT", "marg_adu_tab"),
                C("FAIXA_INI_FERT", "inim_adu_tab"),
                C("FAIXA_FIM_FERT", "fimm_adu_tab"),
                C("MARGEM_DEF", "marg_qui_tab"),
                C("FAIXA_INI_DEF", "inim_qui_tab"),
                C("FAIXA_FIM_DEF", "fimm_qui_tab"),
                C("MARGEM_ESP", "marg_nut_tab"),
                C("FAIXA_INI_ESP", "inim_nut_tab"),
                C("FAIXA_FIM_ESP", "fimm_nut_tab"),
                C("MARGEM_SEM", "marg_sem_tab"),
                C("FAIXA_INI_SEM", "inim_sem_tab"),
                C("FAIXA_FIM_SEM", "fimm_sem_tab"),
            }),

            [PrecoItens] = new("AGROINSU.V_AX_ITABELA_PRECO", null, new[]
            {
                C("ID_CTAB", "erp_id"),
                C("CODI_PSV", "codi_pro"),
                // Insumos do motor (APEX_ITAB_VIG). CUSTO vai para cust_itp;
                // prrs_itp/prus_itp deixaram de vir do ERP (viram snapshot do app).
                C("CUSTO", "cust_itp"),
                C("EMBALAGEM", "emba_itp"),
                C("ORIGEM", "orig_itp"),
                C("ADICIONAL", "adic_itp"),
                C("FABRICANTE", "fabr_itp"),
            }),

            ["condicao"] = new("AGROINSU.V_AX_CONDICAO", "condicoes", new[]
            {
                C("COND_CON", "cond_con"),
                C("DESC_CON", "desc_con"),
                C("CODI_IND", "codi_ind"),
                C("DESC_IND", "desc_ind"),
                C("STAT_CON", "situ_con"),
            }),

            ["carteira"] = new("AGROINSU.V_AX_CARTEIRA", "carteira", new[]
            {
                // Chave natural do ERP (CODI_EMP|PEDI_PED|SERI_PED|CODI_PSV) ja concatenada
                // na view como ORIGEM_ERP_ID -> vira origem_erp_id da solicitacao no AceleraX.
                C("ORIGEM_ERP_ID", "origem_erp_id"),
                C("CODI_EMP", "codi_emp"),
                C("PEDIDO", "pedido"),
                C("CODI_TRA", "codi_tra"),
                C("PROP_PRO", "prop_pro"),
                C("CODI_PES", "codi_pes"),
                C("CODI_PSV", "codi_psv"),
                C("CODI_CIC", "codi_cic"),
                C("QTDE", "qtde_pedida"),
                C("ENTREGUE", "qtde_entregue"),
                C("SALDO", "saldo_aberto"),
                C("EF_GLOBAL", "estoque_global"),
                C("EF_LOJA", "estoque_loja"),
            }),
        };
}
