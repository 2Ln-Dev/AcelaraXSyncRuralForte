"""
UPSERT keys and De/Para column mappings per spec.
"""

TABLES_SPEC = {
    "transac": {
        "oracle_view": "HUBRAIZ.V_TRANSAC",
        "supabase_table": "transac",
        "upsert_key": ("codi_ten", "codi_tra"),
        "columns": {
            "codi_ten": "codi_ten",
            "codi_tra": "codi_tra",
            "nome_tra": "nome_tra",
        }
    },
    "propried": {
        "oracle_view": "HUBRAIZ.V_PROPRIED",
        "supabase_table": "propried",
        "upsert_key": ("codi_ten", "prop_pro"),
        "columns": {
            "codi_ten": "codi_ten",
            "prop_pro": "prop_pro",
            "codi_tra": "codi_tra",
        }
    },
    "ciclos_safra": {
        "oracle_view": "HUBRAIZ.V_APEX_CICLO",
        "supabase_table": "ciclos_safra",
        "upsert_key": ("codi_ten", "nome_csa"),
        "columns": {
            "codi_ten": "codi_ten",
            "nome_csa": "nome_csa",
        }
    },
    "estoque": {
        "oracle_view": "HUBRAIZ.V_EST_CICLO_AF2",
        "supabase_table": "estoque",
        "upsert_key": ("codi_ten", "codi_pro", "codi_fil", "codi_csa"),
        "columns": {
            "codi_ten": "codi_ten",
            "codi_pro": "codi_pro",
            "codi_fil": "codi_fil",
            "codi_csa": "codi_csa",
        }
    },
    "prodserv": {
        "oracle_view": "HUBRAIZ.V_PRODUTO_PRODSERV",
        "supabase_table": "prodserv",
        "upsert_key": ("codi_ten", "codi_psv"),
        "columns": {
            "codi_ten": "codi_ten",
            "codi_psv": "codi_psv",
            "codi_gpr": "codi_gpr",
            "codi_sbg": "codi_sbg",
        }
    },
    "grupo": {
        "oracle_view": "HUBRAIZ.V_APEX_GRUPO",
        "supabase_table": "grupo",
        "upsert_key": ("codi_ten", "codi_gpr"),
        "columns": {
            "codi_ten": "codi_ten",
            "codi_gpr": "codi_gpr",
            "nome_gpr": "nome_gpr",
        }
    },
    "subgrupo": {
        "oracle_view": "HUBRAIZ.V_APEX_SUBGRUPO",
        "supabase_table": "subgrupo",
        "upsert_key": ("codi_ten", "codi_sbg"),
        "columns": {
            "codi_ten": "codi_ten",
            "codi_sbg": "codi_sbg",
            "codi_gpr": "codi_gpr",
            "nome_sbg": "nome_sbg",
        }
    },
    "tabelas_preco": {
        "oracle_view": "HUBRAIZ.V_VTABELA_SINC",
        "supabase_table": "tabelas_preco",
        "upsert_key": ("codi_ten", "nome_tab"),
        "is_header": True,
        "columns": {
            "codi_ten": "codi_ten",
            "id_ctab": "codi_tab",
            "nome_tab": "nome_tab",
        }
    },
    "itens_tabela_preco": {
        "oracle_view": "HUBRAIZ.V_VTABELA_SINC",
        "supabase_table": "itens_tabela_preco",
        "upsert_key": ("codi_tab", "codi_pro"),
        "is_items": True,
        "columns": {
            "codi_tab": "codi_tab",
            "codi_psv": "codi_pro",
        }
    }
}

SYNC_ORDER = [
    "transac",
    "propried",
    "grupo",
    "subgrupo",
    "prodserv",
    "ciclos_safra",
    "estoque",
    "tabelas_preco",
    "itens_tabela_preco",
]
