using System.Globalization;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using PedidoErp.Service.Core.Data;

namespace PedidoErp.Service.Tabela;

/// <summary>Erro de negocio ao materializar a tabela barter (equivale ao ErroNegocio Python).</summary>
public sealed class ErroNegocioTabela : Exception
{
    public ErroNegocioTabela(string msg, string? tipo = null) : base(msg) => Tipo = tipo;

    /// <summary>Tipo de erro para o AceleraX (POST /erro). Null = o worker classifica pela mensagem.</summary>
    public string? Tipo { get; }
}

/// <summary>
/// Materializa as tabelas de preco barter ANTES da DLL: preenche codigoTabelaPreco
/// (por item) e dataCotacao (no cabecalho) dos itens barter. Port in-process do
/// gerar_tabela_api.py + gerar_tabela_pricing.py (o /gerar-tabela HTTP some no x86).
///
/// ⚠️ A formula de precificacao e' a MESMA do appAgroinsumos/gerar_tabela_pricing.py.
/// Se mudar la, replicar aqui. O pedido_erp nunca envia id_modelo, entao o ramo de
/// "copiar CABTAB modelo" NAO e' portado (a CABTAB nasce so com os overrides fixos).
/// </summary>
public interface ITabelaBarterService
{
    Task PreencherAsync(JsonObject payload, CancellationToken ct);
}

public sealed class TabelaBarterService : ITabelaBarterService
{
    // Imposto de venda (Parametros!J9): entra no preco junto da margem.
    private const double ImpostoVenda = 0.00925;
    private const string OrigemChamada = "pedido_erp";
    private static readonly DateTime DataVlrIni = new(1900, 1, 1);
    private static readonly DateTime DataVlrFim = new(1940, 12, 31);

    // Segmento (APEX_GRUPO.desc_gpr) -> sufixo das colunas em APEX_GER_TABELAS.
    private static readonly Dictionary<string, string> MapSeg = new()
    {
        ["01-DEFENSIVOS"] = "DEF",
        ["02-SEMENTES"] = "SEM",
        ["03-ESPECIALIDADES"] = "ESP",
        ["04-FERTILIZANTES"] = "FERT",
    };

    private const string HoraSql = "TO_DSINTERVAL('0 ' || TO_CHAR(SYSDATE,'HH24:MI:SS'))";

    // ── SQL de leitura (verbatim do Python) ─────────────────────────────
    private const string QResolveVersao =
        "SELECT ID FROM AGROINSU.APEX_GER_TABELAS WHERE ID_TAB_DEST = :t";

    private const string QIdempotencia = @"
SELECT L.ORIGEM, L.ORIGEM_COD, L.TABE_CTA, L.QTD_ITENS
FROM   AGROINSU.APEX_GERTAB_LOG L
JOIN   CABTAB C ON C.TABE_CTA = L.TABE_CTA
WHERE  L.ID_GER = :id_ger
  AND  L.MOEDA_ALVO = :moeda
  AND  NVL(L.COMMODITY,0) = NVL(:commodity,0)
  AND  L.CONDICAO = :condicao
  AND  NVL(L.RT_REAIS,0) = NVL(:rt_reais,0)
  AND  NVL(L.RT_DOLAR,0) = NVL(:rt_dolar,0)
  AND  NVL(C.CODI_EMP,-1) = NVL(:codi_emp,-1)
  AND  L.STATUS = 'GERADA'
ORDER BY L.ORIGEM_COD";

    private const string QVersao = @"
SELECT G.ID, G.NOVA_VERSAO, G.ID_TAB_DEST, CIC.DESC_CIC,
       G.DATA_BASE, G.VENC_MAX, G.PTAX_VISTA, G.PTAX_PRAZO,
       G.CORREC_RS, G.ANTEC_RS, G.CORREC_USS, G.ANTEC_USS,
       G.MARGEM_DEF, G.FAIXA_INI_DEF, G.FAIXA_FIM_DEF,
       G.MARGEM_SEM, G.FAIXA_INI_SEM, G.FAIXA_FIM_SEM,
       G.MARGEM_ESP, G.FAIXA_INI_ESP, G.FAIXA_FIM_ESP,
       G.MARGEM_FERT, G.FAIXA_INI_FERT, G.FAIXA_FIM_FERT,
       G.DATA_FINALIZACAO
FROM   APEX_GER_TABELAS G
INNER  JOIN CICLO CIC ON CIC.CODI_CIC = G.CODI_CIC
WHERE  G.ID_TAB_DEST = :id";

    private const string QItens = @"
SELECT VP.CODI_PSV, VP.DESC_PSV, AG.DESC_GPR, IV.ORIGEM, IV.CUSTO, IV.ADICIONAL
FROM   APEX_ITAB_VIG IV
INNER  JOIN VPRD_PRINCIPAL VP ON VP.CODI_PSV_PRI = IV.CODI_PSV
INNER  JOIN PRODSERV PSV ON PSV.CODI_PSV = IV.CODI_PSV
LEFT   JOIN APEX_GRUPO AG ON AG.CHAVEGRUPO = PSV.CODI_GPR || '#' || PSV.CODI_SBG
WHERE  IV.ID_CTAB = :id_ctab
ORDER  BY IV.ORIGEM, VP.DESC_PSV";

    // Pre-checagem: (produto, origem) que a versao consegue precificar. MESMA expansao
    // principal->derivados da QItens — se sair daqui, sai da tabela gerada.
    private const string QProdutosVersao = @"
SELECT VP.CODI_PSV, IV.ORIGEM
FROM   APEX_ITAB_VIG IV
INNER  JOIN VPRD_PRINCIPAL VP ON VP.CODI_PSV_PRI = IV.CODI_PSV
INNER  JOIN PRODSERV PSV ON PSV.CODI_PSV = IV.CODI_PSV
WHERE  IV.ID_CTAB = :id_ctab";

    private const string QNomeVersao =
        "SELECT NOVA_VERSAO FROM APEX_GER_TABELAS WHERE ID_TAB_DEST = :id";

    private const string QDataVlr = "SELECT DATA_VLR FROM CABTAB WHERE TABE_CTA = :t";
    private const string QIndexadorDesc = "SELECT DESC_IND FROM INDEXADOR WHERE CODI_IND = :i";

    // Indexador de PTAX do ERP — o mesmo que o MergePtax usa. E' a CONDICAO de venda em
    // dolar que aponta para ele (CONDICAO.CODI_IND), por isso a tabela em US$ precisa
    // carregar esse indexador para o ERP achar a cotacao.
    private const string QIndexadorPtax =
        "SELECT MIN(INDX_PTC) FROM PARAMGERTRC WHERE INDX_PTC IS NOT NULL";
    private const string QProxDataVlr =
        "SELECT DISTINCT TRUNC(DATA_VLR) DV FROM INDVALOR WHERE CODI_IND = :i AND DATA_VLR BETWEEN :ini AND :fim";

    // ── SQL de escrita ──────────────────────────────────────────────────
    private const string MergeIndvalor = @"
MERGE INTO INDVALOR d
USING (SELECT CODI_EMP FROM CADEMP) e
  ON (d.CODI_IND = :ind AND d.CODI_EMP = e.CODI_EMP AND TRUNC(d.DATA_VLR) = TRUNC(:dt))
WHEN MATCHED THEN UPDATE SET d.VLOR_VLR = :vlr
WHEN NOT MATCHED THEN
  INSERT (CODI_IND, CODI_EMP, DATA_VLR, VLOR_VLR)
  VALUES (:ind, e.CODI_EMP, TRUNC(:dt), :vlr)";

    private const string MergePtax = @"
MERGE INTO INDVALOR d
USING (SELECT p.CODI_EMP, p.INDX_PTC
         FROM PARAMGERTRC p WHERE p.INDX_PTC IS NOT NULL) e
  ON (d.CODI_IND = e.INDX_PTC AND d.CODI_EMP = e.CODI_EMP AND TRUNC(d.DATA_VLR) = TRUNC(:dt))
WHEN NOT MATCHED THEN
  INSERT (CODI_IND, CODI_EMP, DATA_VLR, VLOR_VLR)
  VALUES (e.INDX_PTC, e.CODI_EMP, TRUNC(:dt), :cambio)";

    private const string InsertTabela = @"
INSERT INTO TABELA
    (TABE_CTA, CODI_PSV, CUST_TAB, TCUS_TAB, MARG_TAB, BASI_TAB,
     ACRE_TAB, DESC_TAB, COMI_TAB, PDES_TAB, PACR_TAB, SITU_TAB)
VALUES
    (:tabe, :psv, :cust, '4', :marg, :basi, 0, 0, 0, :pdes, :pacr, 'A')";

    private const string InsertHistCab = @"
INSERT INTO HISTORICOCABTAB (CTRL_HCT, TABE_CTA, DATA_HCT, HORA_HCT, ACAO_HCT)
VALUES (:ctrl, :tabe, SYSDATE, " + HoraSql + ", 'I')";

    private const string InsertHistTab = @"
INSERT INTO HISTORICOTABELA
    (CTRL_HTB, CODI_PSV, TABE_CTA, DATA_HTB, HORA_HTB,
     TCUS_TAB, CUST_TAB, MARG_TAB, BASI_TAB, COMI_TAB, SITU_TAB, ACAO_HTB)
VALUES (:ctrl, :psv, :tabe, SYSDATE, " + HoraSql + @",
     '4', :cust, :marg, :basi, 0, 'A', 'I')";

    private const string InsertLog = @"
INSERT INTO AGROINSU.APEX_GERTAB_LOG
    (ID, DATA_HORA, ID_GER, CICLO, MOEDA_ALVO, COMMODITY, CONDICAO,
     ORIGEM, ORIGEM_COD, RT_REAIS, RT_DOLAR, CAMBIO, TABE_CTA, QTD_ITENS,
     STATUS, ORIGEM_CHAMADA)
VALUES
    (:id, SYSTIMESTAMP, :id_ger, :ciclo, :moeda, :commodity, :condicao,
     :origem, :origem_cod, :rt_reais, :rt_dolar, :cambio, :tabe, :qtd,
     'GERADA', :chamada)";

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<TabelaBarterService> _log;

    public TabelaBarterService(IDbConnectionFactory db, ILogger<TabelaBarterService> log)
    {
        _db = db;
        _log = log;
    }

    // ── Entrada ─────────────────────────────────────────────────────────
    public async Task PreencherAsync(JsonObject payload, CancellationToken ct)
    {
        var itens = payload["pedidoVendaItensList"]?.AsArray();
        if (itens is null) return;

        var codiEmp = ParseCodiEmp(payload["codigoEmpresa"]);

        // 1) Agrupa por receita os itens que ainda precisam de tabela.
        var receitas = new Dictionary<string, Pendencia>();
        foreach (var it in itens)
        {
            if (it is null) continue;
            if (!VazioTabela(it["codigoTabelaPreco"])) continue;      // ja tem tabela (nao-barter)
            if (it["gerarTabela"] is not JsonObject gt || VazioTabela(gt["idGer"])) continue; // sem receita

            var r = Recipe.From(gt, codiEmp);
            if (!receitas.TryGetValue(r.Key, out var p))
                receitas[r.Key] = p = new Pendencia(r);
            p.Itens.Add(it);
        }
        if (receitas.Count == 0) return;

        // 2) Confere TODOS os produtos ANTES de gerar. Sem isso o pedido so' quebra la' na
        //    DLL, que acusa UM produto por vez (RCTA013) e ja' com a CABTAB commitada —
        //    cada pedido recusado deixava 2 tabelas orfas no ERP.
        foreach (var p in receitas.Values)
            await ValidarProdutosAsync(p, ct);

        // 3) Gera/reaproveita e preenche.
        string? dataCotacao = null;
        foreach (var p in receitas.Values)
        {
            var r = p.Recipe;
            var res = await GerarOuReaproveitarAsync(r, ct);
            _log.LogInformation(
                "gerar_tabela idGer={IdGer} codi_emp={CodiEmp} moeda={Moeda} cond={Cond} status={Status} reais={Reais} dolar={Dolar} data={Data}",
                r.IdGer, r.CodiEmp, r.MoedaAlvo, r.Condicao, res.Status, res.Reais, res.Dolar, res.DataCotacao);

            foreach (var it in p.Itens)
            {
                var origem = it["origem"]?.ToString();
                int? tabe = origem == "R$" ? res.Reais : origem == "US$" ? res.Dolar : null;
                if (tabe is null or 0)
                    throw new ErroNegocioTabela(
                        $"sem tabela p/ origem {origem} (idGer {r.IdGer}, produto {it["codigoProduto"]})");

                it["codigoTabelaPreco"] = tabe.Value;
            }
            if (!string.IsNullOrEmpty(res.DataCotacao)) dataCotacao = res.DataCotacao;
        }

        if (!string.IsNullOrEmpty(dataCotacao))
            payload["dataCotacao"] = $"{dataCotacao} 00:00:00";
    }

    // ── Pre-checagem dos produtos (antes de gravar qualquer coisa) ───────
    /// <summary>
    /// Falha listando TODOS os produtos do pedido que nao sairiam precificados nesta versao
    /// — fora da versao, origem invalida, ou sem RT informada para a origem (barter).
    /// Somente leitura: nada e' gravado enquanto isso nao passar.
    /// </summary>
    private async Task ValidarProdutosAsync(Pendencia p, CancellationToken ct)
    {
        var r = p.Recipe;
        using var conn = await _db.CreateOpenAsync(ct);

        var disponiveis = (await conn.QueryAsync(QProdutosVersao, Bind(new { id_ctab = r.IdGer })))
            .Cast<IDictionary<string, object>>()
            .Select(row => Chave(Str(row, "CODI_PSV"), Str(row, "ORIGEM")))
            .ToHashSet(StringComparer.Ordinal);

        // Versao inexistente/vazia: deixa o fluxo normal reportar o motivo real
        // ("Versão não encontrada", "ainda não está totalmente liberada", ...).
        if (disponiveis.Count == 0) return;

        var faltando = new List<string>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in p.Itens)
        {
            var produto = it["codigoProduto"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(produto)) continue;
            var origem = it["origem"]?.ToString()?.Trim();

            string? motivo = null;
            if (origem is not ("R$" or "US$"))
                motivo = $"origem {(string.IsNullOrEmpty(origem) ? "vazia" : origem)}";
            else if (r.MoedaAlvo == "BARTER" && IsZero(origem == "R$" ? r.RtReais : r.RtDolar))
                motivo = $"sem RT {origem} informada";
            else if (!disponiveis.Contains(Chave(produto, origem)))
                motivo = $"fora da versão ({origem})";

            if (motivo is not null && vistos.Add(produto))
                faltando.Add($"{produto} [{motivo}]");
        }
        if (faltando.Count == 0) return;

        var versao = await conn.QueryFirstOrDefaultAsync<string?>(QNomeVersao, Bind(new { id = r.IdGer }));
        throw new ErroNegocioTabela(
            $"produtos fora da tabela de preço {versao ?? r.IdGer.ToString(CultureInfo.InvariantCulture)}: " +
            string.Join(", ", faltando),
            "PRODUTO_FORA_TABELA");
    }

    // CODI_PSV comparado EXATO, com os zeros a esquerda: no cadastro '0001245' (ADUBO MAP)
    // e '1245' (FINALE 5 LT) sao produtos DIFERENTES — normalizar juntaria os dois.
    private static string Chave(string? codiPsv, string? origem) =>
        (codiPsv?.Trim() ?? "") + "|" + (origem ?? "");

    // ── /gerar-tabela in-process (idempotencia + geracao numa transacao) ─
    private async Task<TabelaResult> GerarOuReaproveitarAsync(Recipe r, CancellationToken ct)
    {
        using var conn = await _db.CreateOpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // id_ger recebido = id da TABELA DE PRECO (ID_TAB_DEST). Idempotencia/log
            // chaveiam pela VERSAO real (APEX_GER_TABELAS.ID) — traduz aqui.
            var versaoId = await conn.QueryFirstOrDefaultAsync<int?>(
                QResolveVersao, Bind(new { t = r.IdGer }), tx);
            if (versaoId is null)
                throw new ErroNegocioTabela("Versão não encontrada.");

            // 1) idempotencia (chaveada pela versao)
            var pIdemp = new OracleDynamicParameters();
            pIdemp.Add("id_ger", versaoId.Value);
            pIdemp.Add("moeda", r.MoedaAlvo);
            pIdemp.Add("commodity", r.Commodity);
            pIdemp.Add("condicao", r.Condicao);
            pIdemp.Add("rt_reais", r.RtReais);
            pIdemp.Add("rt_dolar", r.RtDolar);
            pIdemp.Add("codi_emp", r.CodiEmp);
            var existentes = (await conn.QueryAsync(QIdempotencia, pIdemp, tx)).ToList();

            if (existentes.Count > 0)
            {
                var res = MontarResultado(existentes.Select(row =>
                    (Cod: (int)Convert.ToInt64(row.ORIGEM_COD), Tabe: (int)Convert.ToInt64(row.TABE_CTA))));
                var tabe0 = existentes.Select(row => (int)Convert.ToInt64(row.TABE_CTA)).First();

                var dataVlr = await conn.QueryFirstOrDefaultAsync<DateTime?>(QDataVlr, Bind(new { t = tabe0 }), tx);
                if (dataVlr is not null && !IsZero(r.Cambio))
                {
                    var pPtax = new OracleDynamicParameters();
                    pPtax.Add("dt", dataVlr.Value);
                    pPtax.Add("cambio", r.Cambio);
                    await conn.ExecuteAsync(MergePtax, pPtax, tx);
                }
                tx.Commit();
                return res with { Status = "REAPROVEITADA", DataCotacao = FmtData(dataVlr) };
            }

            // 2) gera + loga (mesma transacao)
            var gerado = await GerarNovaAsync(conn, tx, r, versaoId.Value, ct);
            tx.Commit();
            return gerado;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ja pode ter commitado/fechado */ }
            throw;
        }
    }

    // ── Geracao de tabela NOVA (port de gt.gerar + _grava_log) ───────────
    private async Task<TabelaResult> GerarNovaAsync(OracleConnection conn, OracleTransaction tx, Recipe r, int versaoId, CancellationToken ct)
    {
        var ver = await CarregaVersaoAsync(conn, tx, r.IdGer);
        var (cambio, rtReais, rtDolar) = ResolveParams(ver, r);
        var label = await MoedaLabelAsync(conn, tx, r);

        var grupos = Calcula(ver, cambio, rtReais, rtDolar, r);
        if (grupos.Count == 0)
            throw new ErroNegocioTabela("Nenhum item com origem R$/US$ para gerar.");

        // Cotacao indexada: a tabela nasce amarrada a um INDEXADOR + DATA_VLR, e o ERP
        // multiplica o valor da tabela (e o do item do pedido) pela cotacao daquela data
        // — ver PPEDIDO_ATUDADOSVENDA: "if (not indexador is null) ... pven_aux * vlorind".
        //
        //   BARTER : BASI_TAB em SACAS  x cotacao da commodity (R$/saca) = R$
        //   US$    : BASI_TAB em DOLAR  x PTAX                           = R$
        //   R$     : sem indexador — o valor ja' esta' em reais (padrao nativo do ERP,
        //            ver as tabelas 69/71 do proprio cliente).
        //
        // Sem isso a DLL recusa o pedido em dolar com INDEXA001 ("Data de referencia da
        // cotacao invalida"), porque a CONDICAO de venda do dolar e' indexada a PTAX
        // (CONDICAO.CODI_IND = PARAMGERTRC.INDX_PTC).
        DateTime? dataVlr = null;
        int? indexador = null;

        if (r.MoedaAlvo == "BARTER")
        {
            indexador = r.Commodity;
            dataVlr = await ProximaDataVlrAsync(conn, tx, r.Commodity);
            var valor = ValorCotacao(r, cambio, rtReais, rtDolar)
                ?? throw new ErroNegocioTabela("Não foi possível calcular o valor da cotação (verifique RT US$/dólar).");
            var pInd = new OracleDynamicParameters();
            pInd.Add("ind", r.Commodity);
            pInd.Add("dt", dataVlr.Value);
            pInd.Add("vlr", valor);
            await conn.ExecuteAsync(MergeIndvalor, pInd, tx);
            // A DLL do pedido tambem le a cotacao do cambio (PTAX) nessa mesma data.
            if (!IsZero(cambio))
            {
                var pPtax = new OracleDynamicParameters();
                pPtax.Add("dt", dataVlr.Value);
                pPtax.Add("cambio", cambio);
                await conn.ExecuteAsync(MergePtax, pPtax, tx);
            }
        }
        else if (r.MoedaAlvo == "US$")
        {
            // Mesmo mecanismo do barter, trocando a commodity pelo indexador de PTAX e a
            // cotacao do grao pelo cambio do pedido.
            indexador = await conn.QueryFirstOrDefaultAsync<int?>(QIndexadorPtax, null, tx)
                ?? throw new ErroNegocioTabela(
                    "PARAMGERTRC.INDX_PTC não configurado — sem indexador de PTAX para a tabela em dólar.");
            if (IsZero(cambio))
                throw new ErroNegocioTabela("Câmbio não informado — necessário para a cotação da tabela em dólar.");

            // Data livre PARA O INDEXADOR DE PTAX (o barter procura livre para a commodity).
            dataVlr = await ProximaDataVlrAsync(conn, tx, indexador);
            var pInd = new OracleDynamicParameters();
            pInd.Add("ind", indexador);
            pInd.Add("dt", dataVlr.Value);
            pInd.Add("vlr", cambio);
            await conn.ExecuteAsync(MergeIndvalor, pInd, tx);
        }

        var geradas = new List<Gerada>();
        foreach (var origem in grupos.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var itens = grupos[origem];
            var newTabe = await CriaCabtabAsync(conn, tx, ver, r, origem, dataVlr, indexador, label);
            await GravaItensAsync(conn, tx, newTabe, itens, ver);
            await GravaHistoricoAsync(conn, tx, newTabe, itens);
            geradas.Add(new Gerada(origem, OrigemCod(origem), newTabe, itens.Count));
        }

        await GravaLogAsync(conn, tx, versaoId, r, cambio, ver.Get<string?>("DESC_CIC"), geradas);

        var result = MontarResultado(geradas.Select(g => (g.Cod, g.Tabe)));
        return result with { Status = "GERADA", DataCotacao = FmtData(dataVlr) };
    }

    // ── Carga da versao / itens ─────────────────────────────────────────
    private async Task<VerRow> CarregaVersaoAsync(OracleConnection conn, OracleTransaction tx, int idGer)
    {
        var row = await conn.QueryFirstOrDefaultAsync(QVersao, Bind(new { id = idGer }), tx);
        if (row is null)
            throw new ErroNegocioTabela("Versão não encontrada.");
        var ver = new VerRow((IDictionary<string, object>)row);
        if (ver.Get<DateTime?>("DATA_FINALIZACAO") is null)
            throw new ErroNegocioTabela("Versão ainda não está totalmente liberada.");
        var itens = (await conn.QueryAsync(QItens, Bind(new { id_ctab = ver.Get<int>("ID_TAB_DEST") }), tx))
            .Cast<IDictionary<string, object>>().ToList();
        ver.Itens = itens;
        return ver;
    }

    private (double? cambio, double? rtReais, double? rtDolar) ResolveParams(VerRow ver, Recipe r)
    {
        if (r.Condicao is not ("VISTA" or "PRAZO"))
            throw new ErroNegocioTabela($"Condição inválida: {r.Condicao}");
        if (r.MoedaAlvo is not ("R$" or "US$" or "BARTER"))
            throw new ErroNegocioTabela($"Moeda-alvo inválida: {r.MoedaAlvo}");

        var cambio = r.Cambio ?? (r.Condicao == "VISTA" ? ver.GetD("PTAX_VISTA") : ver.GetD("PTAX_PRAZO"));
        if (r.MoedaAlvo == "BARTER")
        {
            if (r.Commodity is null or 0) throw new ErroNegocioTabela("Selecione a commodity (indexador) para Barter.");
            if (IsZero(r.RtReais) && IsZero(r.RtDolar)) throw new ErroNegocioTabela("Informe pelo menos uma RT (R$ ou US$) para Barter.");
            if (cambio is null) throw new ErroNegocioTabela("Câmbio (dólar) não informado e sem PTAX na versão.");
        }
        else if (cambio is null)
        {
            throw new ErroNegocioTabela("Câmbio (dólar) não informado e sem PTAX na versão.");
        }
        return (cambio, r.RtReais, r.RtDolar);
    }

    // ── Nucleo da precificacao (port fiel) ──────────────────────────────
    private static double Round6(double v) => Math.Round(v, 6, MidpointRounding.ToEven);

    private static double ConvMoeda(double valor, string origem, string moedaAlvo, double cambio)
    {
        if (moedaAlvo == "R$") return origem == "R$" ? valor : valor * cambio;
        if (moedaAlvo == "US$") return origem == "US$" ? valor : valor / cambio;
        throw new ErroNegocioTabela($"Conversão de moeda inválida: {moedaAlvo}");
    }

    private static double FatorCorrecao(double meses, string origem, double? correcRs, double? antecRs, double? correcUss, double? antecUss)
    {
        double? taxa = origem == "R$"
            ? (meses >= 0 ? correcRs : antecRs)
            : (meses >= 0 ? correcUss : antecUss);
        return 1 + meses * ((taxa ?? 0) / 100.0);
    }

    private static (double basi, double cust, double margEf) PrecoItem(
        string origem, double? custo, double? adicional, double? margemPct, double meses,
        double? correcRs, double? antecRs, double? correcUss, double? antecUss,
        string moedaAlvo, double? cambio, double? rtReais, double? rtDolar)
    {
        var c = custo ?? 0;
        var a = adicional ?? 0;
        var divisor = 1 - (margemPct ?? 0) / 100.0 - ImpostoVenda;
        var margEf = Round6((margemPct ?? 0) + ImpostoVenda * 100);

        if (moedaAlvo == "BARTER")
        {
            var rt = origem == "R$" ? rtReais : rtDolar;
            if (IsZero(rt)) throw new ErroNegocioTabela($"Informe a RT para produtos com origem {origem}.");
            var novoCusto = (c + a) / rt!.Value;
            var basiB = divisor > 0 ? novoCusto / divisor : novoCusto;
            return (Round6(basiB), Round6(novoCusto), margEf);
        }

        // A GERACAO DE TABELA NAO aplica correcao financeira: o custo vai para a tabela
        // como esta' na versao, so' convertido pelo cambio. FatorCorrecao/Meses continuam
        // no arquivo porque a correcao segue valendo onde ela e' regra (fluxo do pedido);
        // aqui ela deixou de ser aplicada de proposito. Espelha gerar_tabela_pricing.py.
        var custoEq = c + a;
        var venda = divisor > 0 ? custoEq / divisor : custoEq;
        var basi = ConvMoeda(venda, origem, moedaAlvo, cambio!.Value);
        var cust = ConvMoeda(custoEq, origem, moedaAlvo, cambio!.Value);
        return (Round6(basi), Round6(cust), margEf);
    }

    private static (double? margem, double? faixaIni, double? faixaFim) ParamSegmento(VerRow ver, string? descGpr)
    {
        if (descGpr is null || !MapSeg.TryGetValue(descGpr, out var seg))
            return (null, null, null);
        return (ver.GetD($"MARGEM_{seg}"), ver.GetD($"FAIXA_INI_{seg}"), ver.GetD($"FAIXA_FIM_{seg}"));
    }

    private static double Meses(VerRow ver)
    {
        var db = ver.Get<DateTime?>("DATA_BASE");
        var vm = ver.Get<DateTime?>("VENC_MAX");
        if (db is null || vm is null) return 0.0;
        return (vm.Value.Date - db.Value.Date).Days / 30.0;
    }

    private Dictionary<string, List<CalcItem>> Calcula(VerRow ver, double? cambio, double? rtReais, double? rtDolar, Recipe r)
    {
        var meses = Meses(ver);
        var grupos = new Dictionary<string, List<CalcItem>>();
        var vistos = new Dictionary<string, HashSet<string>>();

        foreach (var it in ver.Itens)
        {
            var origem = Str(it, "ORIGEM");
            if (origem is not ("R$" or "US$")) continue;
            if (r.MoedaAlvo == "BARTER" && IsZero(origem == "R$" ? rtReais : rtDolar)) continue;

            var codiPsv = Str(it, "CODI_PSV") ?? "";
            var seen = vistos.TryGetValue(origem, out var hs) ? hs : (vistos[origem] = new HashSet<string>());
            if (!seen.Add(codiPsv)) continue;

            var descGpr = Str(it, "DESC_GPR");
            var (margem, _, _) = ParamSegmento(ver, descGpr);
            var (basi, cust, margEf) = PrecoItem(
                origem, Dbl(it, "CUSTO"), Dbl(it, "ADICIONAL"), margem, meses,
                ver.GetD("CORREC_RS"), ver.GetD("ANTEC_RS"), ver.GetD("CORREC_USS"), ver.GetD("ANTEC_USS"),
                r.MoedaAlvo, cambio, rtReais, rtDolar);

            (grupos.TryGetValue(origem, out var lst) ? lst : (grupos[origem] = new List<CalcItem>()))
                .Add(new CalcItem(codiPsv, descGpr, origem, margEf, cust, basi));
        }
        return grupos;
    }

    // ── Cabecalho / cotacao ─────────────────────────────────────────────
    private static double? ValorCotacao(Recipe r, double? cambio, double? rtReais, double? rtDolar)
    {
        if (r.MoedaAlvo != "BARTER") return null;
        if (!IsZero(rtDolar) && !IsZero(cambio)) return Round6(rtDolar!.Value * cambio!.Value);
        if (!IsZero(rtReais)) return Round6(rtReais!.Value);
        return null;
    }

    private async Task<string> MoedaLabelAsync(OracleConnection conn, OracleTransaction tx, Recipe r)
    {
        if (r.MoedaAlvo is "R$" or "US$") return r.MoedaAlvo;
        var d = await conn.QueryFirstOrDefaultAsync<string?>(QIndexadorDesc, Bind(new { i = r.Commodity }), tx) ?? "BARTER";
        var label = d.Split('(')[0].Trim();
        return string.IsNullOrEmpty(label) ? "BARTER" : label;
    }

    private static string DescTabela(VerRow ver, Recipe r, string origem, string label)
    {
        var baseTxt = $"{ver.Get<string?>("NOVA_VERSAO") ?? ""} {label} {r.Condicao} ORIG {origem}".Trim();
        return baseTxt.Length > 40 ? baseTxt[..40] : baseTxt;
    }

    private async Task<DateTime> ProximaDataVlrAsync(OracleConnection conn, OracleTransaction tx, int? commodity)
    {
        var usados = (await conn.QueryAsync<DateTime>(QProxDataVlr,
                Bind(new { i = commodity, ini = DataVlrIni, fim = DataVlrFim }), tx))
            .Select(d => d.Date).ToHashSet();
        for (var d = DataVlrIni; d <= DataVlrFim; d = d.AddDays(1))
            if (!usados.Contains(d)) return d;
        throw new ErroNegocioTabela("Sem data livre entre 1900 e 1940 para a cotação do indexador.");
    }

    // ── Gravacao ────────────────────────────────────────────────────────
    /// <summary>Cria a CABTAB. `indexador`/`dataVlr` vem nulos em R$ (tabela nao indexada) e
    /// preenchidos em BARTER (commodity) e US$ (PTAX) — ver GerarNovaAsync.</summary>
    private async Task<int> CriaCabtabAsync(OracleConnection conn, OracleTransaction tx, VerRow ver, Recipe r, string origem, DateTime? dataVlr, int? indexador, string label)
    {
        var newTabe = (int)Convert.ToInt64(
            await conn.ExecuteScalarAsync<object>("SELECT GEN_CABTAB_TABE_CTA.NEXTVAL FROM DUAL", null, tx));

        // modelo={} (pedido_erp nao envia id_modelo): CABTAB nasce so com os overrides.
        var cols = new List<string> { "TABE_CTA", "DESC_CTA", "VMAX_CTA", "DTBA_CTA", "DATA_VLR", "CODI_IND",
            "SITU_CTA", "FAPV_CTA", "FATV_CTA", "TCOM_CTA", "VALI_CTA" };
        var p = new OracleDynamicParameters();
        p.Add("TABE_CTA", newTabe);
        p.Add("DESC_CTA", DescTabela(ver, r, origem, label));
        p.Add("VMAX_CTA", ver.Get<DateTime?>("VENC_MAX"));
        p.Add("DTBA_CTA", ver.Get<DateTime?>("DATA_BASE"));
        p.Add("DATA_VLR", dataVlr);
        p.Add("CODI_IND", indexador);
        p.Add("SITU_CTA", "A");
        p.Add("FAPV_CTA", origem == "R$" ? ver.GetD("CORREC_RS") : ver.GetD("CORREC_USS"));
        p.Add("FATV_CTA", origem == "R$" ? ver.GetD("ANTEC_RS") : ver.GetD("ANTEC_USS"));
        p.Add("TCOM_CTA", "11");                 // NOT NULL sem default no banco
        p.Add("VALI_CTA", ver.Get<DateTime?>("VENC_MAX"));  // RCTA004 se null
        if (r.CodiEmp is not null)
        {
            cols.Add("CODI_EMP");
            p.Add("CODI_EMP", r.CodiEmp);
        }

        var colSql = string.Join(", ", cols) + ", DCAD_CTA";
        var valSql = string.Join(", ", cols.Select(c => ":" + c)) + ", SYSDATE";
        await conn.ExecuteAsync($"INSERT INTO CABTAB ({colSql}) VALUES ({valSql})", p, tx);
        return newTabe;
    }

    // Array binding: os N itens vao em 1 round-trip (equivale ao executemany do Python).
    // Mesma SQL (InsertTabela), mesmos campos/valores/transacao — so' muda o transporte.
    private async Task GravaItensAsync(OracleConnection conn, OracleTransaction tx, int newTabe, List<CalcItem> itens, VerRow ver)
    {
        if (itens.Count == 0) return;
        var n = itens.Count;
        var tabe = new long[n]; var psv = new string?[n];
        var cust = new double?[n]; var marg = new double?[n]; var basi = new double?[n];
        var pdes = new double?[n]; var pacr = new double?[n];
        for (var i = 0; i < n; i++)
        {
            var it = itens[i];
            var (_, faixaIni, faixaFim) = ParamSegmento(ver, it.Grupo);
            tabe[i] = newTabe;
            psv[i] = it.CodiPsv;
            cust[i] = it.CustTab; marg[i] = it.Margem; basi[i] = it.BasiTab;
            pdes[i] = faixaIni; pacr[i] = faixaFim;
        }
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertTabela;
        cmd.BindByName = true;
        cmd.ArrayBindCount = n;
        ArrInt(cmd, "tabe", tabe);
        ArrStr(cmd, "psv", psv);
        ArrNum(cmd, "cust", cust);
        ArrNum(cmd, "marg", marg);
        ArrNum(cmd, "basi", basi);
        ArrNum(cmd, "pdes", pdes);
        ArrNum(cmd, "pacr", pacr);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task GravaHistoricoAsync(OracleConnection conn, OracleTransaction tx, int newTabe, List<CalcItem> itens)
    {
        var ctrlHct = (int)Convert.ToInt64(
            await conn.ExecuteScalarAsync<object>("SELECT NVL(MAX(CTRL_HCT),0)+1 FROM HISTORICOCABTAB", null, tx));
        var pCab = new OracleDynamicParameters();
        pCab.Add("ctrl", ctrlHct);
        pCab.Add("tabe", newTabe);
        await conn.ExecuteAsync(InsertHistCab, pCab, tx);

        if (itens.Count == 0) return;
        var baseHtb = (long)Convert.ToInt64(
            await conn.ExecuteScalarAsync<object>("SELECT NVL(MAX(CTRL_HTB),0) FROM HISTORICOTABELA", null, tx));

        // Array binding: N itens em 1 round-trip. Mesma SQL/campos/valores; CTRL_HTB segue
        // sequencial (baseHtb + i + 1), igual ao loop anterior.
        var n = itens.Count;
        var ctrl = new long[n]; var psv = new string?[n]; var tabe = new long[n];
        var cust = new double?[n]; var marg = new double?[n]; var basi = new double?[n];
        for (var i = 0; i < n; i++)
        {
            var it = itens[i];
            ctrl[i] = baseHtb + i + 1;
            psv[i] = it.CodiPsv; tabe[i] = newTabe;
            cust[i] = it.CustTab; marg[i] = it.Margem; basi[i] = it.BasiTab;
        }
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertHistTab;
        cmd.BindByName = true;
        cmd.ArrayBindCount = n;
        ArrInt(cmd, "ctrl", ctrl);
        ArrStr(cmd, "psv", psv);
        ArrInt(cmd, "tabe", tabe);
        ArrNum(cmd, "cust", cust);
        ArrNum(cmd, "marg", marg);
        ArrNum(cmd, "basi", basi);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Array binding helpers (ODP.NET) ─────────────────────────────────
    private static void ArrStr(OracleCommand cmd, string name, string?[] values) =>
        cmd.Parameters.Add(new OracleParameter(name, OracleDbType.Varchar2) { Value = values });

    private static void ArrInt(OracleCommand cmd, string name, long[] values) =>
        cmd.Parameters.Add(new OracleParameter(name, OracleDbType.Int64) { Value = values });

    // NUMBER (6 casas). Nulos -> DBNull (preserva PDES/PACR nulos). Decimal = valor exato.
    private static void ArrNum(OracleCommand cmd, string name, double?[] values)
    {
        var arr = new object[values.Length];
        for (var i = 0; i < values.Length; i++)
            arr[i] = values[i].HasValue ? Convert.ToDecimal(values[i]!.Value) : (object)DBNull.Value;
        cmd.Parameters.Add(new OracleParameter(name, OracleDbType.Decimal) { Value = arr });
    }

    private async Task GravaLogAsync(OracleConnection conn, OracleTransaction tx, int versaoId, Recipe r, double? cambio, string? ciclo, List<Gerada> geradas)
    {
        var baseId = Convert.ToInt64(
            await conn.ExecuteScalarAsync<object>("SELECT NVL(MAX(ID),0) FROM AGROINSU.APEX_GERTAB_LOG", null, tx));
        for (var i = 0; i < geradas.Count; i++)
        {
            var g = geradas[i];
            var p = new OracleDynamicParameters();
            p.Add("id", baseId + i + 1);
            p.Add("id_ger", versaoId);
            p.Add("ciclo", ciclo);
            p.Add("moeda", r.MoedaAlvo);
            p.Add("commodity", r.Commodity);
            p.Add("condicao", r.Condicao);
            p.Add("origem", g.Origem);
            p.Add("origem_cod", g.Cod);
            p.Add("rt_reais", r.RtReais);
            p.Add("rt_dolar", r.RtDolar);
            p.Add("cambio", cambio);
            p.Add("tabe", g.Tabe);
            p.Add("qtd", g.Qtd);
            p.Add("chamada", OrigemChamada);
            await conn.ExecuteAsync(InsertLog, p, tx);
        }
    }

    // ── helpers de tipo ─────────────────────────────────────────────────
    private static OracleDynamicParameters Bind(object template) => new(template);

    private static bool IsZero(double? x) => x is null || x.Value == 0;

    private static bool VazioTabela(JsonNode? n)
    {
        if (n is null) return true;
        var s = n.ToString();
        return string.IsNullOrEmpty(s) || s == "0" || s == "0.0";
    }

    private static int OrigemCod(string origem) => origem == "R$" ? 1 : 2;

    private TabelaResult MontarResultado(IEnumerable<(int Cod, int Tabe)> rows)
    {
        int? reais = null, dolar = null;
        foreach (var (cod, tabe) in rows)
        {
            if (cod == 1) reais = tabe;
            else if (cod == 2) dolar = tabe;
        }
        return new TabelaResult("", reais, dolar, null);
    }

    private static int? ParseCodiEmp(JsonNode? n)
    {
        var s = n?.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        return AsInt(n);
    }

    private static string? FmtData(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string? Str(IDictionary<string, object> r, string k) =>
        r.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;

    private static double? Dbl(IDictionary<string, object> r, string k) =>
        r.TryGetValue(k, out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;

    internal static int? AsInt(JsonNode? n)
    {
        var s = n?.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (int)d : null;
    }

    internal static double? AsDouble(JsonNode? n)
    {
        var s = n?.ToString();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    // ── tipos internos ──────────────────────────────────────────────────
    private sealed class VerRow
    {
        private readonly IDictionary<string, object> _d;
        public VerRow(IDictionary<string, object> d) => _d = d;
        public List<IDictionary<string, object>> Itens { get; set; } = new();

        public T Get<T>(string k)
        {
            if (!_d.TryGetValue(k, out var v) || v is null) return default!;
            var t = typeof(T);
            var target = Nullable.GetUnderlyingType(t) ?? t;
            return (T)Convert.ChangeType(v, target, CultureInfo.InvariantCulture);
        }

        public double? GetD(string k) =>
            _d.TryGetValue(k, out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Itens do pedido que compartilham a mesma receita (mesma tabela gerada).</summary>
    private sealed record Pendencia(Recipe Recipe)
    {
        public List<JsonNode> Itens { get; } = new();
    }

    private sealed record CalcItem(string CodiPsv, string? Grupo, string Origem, double Margem, double CustTab, double BasiTab);

    private sealed record Gerada(string Origem, int Cod, int Tabe, int Qtd);

    private sealed record Recipe(
        int IdGer, string? Condicao, string MoedaAlvo, int? Commodity,
        double? RtReais, double? RtDolar, double? Cambio, int? CodiEmp)
    {
        public string Key => string.Join('|', IdGer, MoedaAlvo, Commodity, RtReais, RtDolar, Cambio, Condicao, CodiEmp);

        public static Recipe From(JsonObject gt, int? codiEmp) => new(
            IdGer: AsInt(gt["idGer"]) ?? 0,
            Condicao: gt["condicao"]?.ToString(),
            MoedaAlvo: gt["moedaAlvo"]?.ToString() ?? "",
            Commodity: AsInt(gt["commodity"]),
            RtReais: AsDouble(gt["rtReais"]),
            RtDolar: AsDouble(gt["rtDolar"]),
            Cambio: AsDouble(gt["cambio"]),
            CodiEmp: codiEmp);
    }

    private sealed record TabelaResult(string Status, int? Reais, int? Dolar, string? DataCotacao);
}
