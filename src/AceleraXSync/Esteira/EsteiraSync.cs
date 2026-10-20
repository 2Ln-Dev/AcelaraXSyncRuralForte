using Dapper;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using AceleraXSync.Acelera;
using AceleraXSync.Core.Data;

namespace AceleraXSync.Esteira;

/// <summary>Resumo de uma rodada (vai para a agenda do AceleraX).</summary>
public sealed record RodadaEsteira(int Recebidas, int Confirmadas, int FasesEnviadas, int FasesComErro)
{
    public int Total => Recebidas + Confirmadas + FasesEnviadas;
    public override string ToString() =>
        $"{Recebidas} solicitação(ões) recebida(s), {Confirmadas} confirmada(s), {FasesEnviadas} fase(s) enviada(s)" +
        (FasesComErro > 0 ? $", {FasesComErro} recusada(s)" : "") + ".";
}

/// <summary>
/// Transporte da Esteira de Faturamento do HUB 2LN (docs/esteira-faturamento/03 do HUB). O Sync NAO aplica regra da
/// esteira — so' leva e traz, pelas tabelas de passagem que o HUB cria (APEX_EST_AX_*, mesmo schema do ERP):
///
///   1. <b>entrada</b> — GET /solicitacoes (as nao integradas) → GET /solicitacoes/:id → INSERT dos itens em
///      APEX_EST_AX_ENTRADA, todos numa transacao (o HUB so' le solicitacao completa). Ja' recebida = pula.
///   2. <b>confirmacao</b> — o HUB processou (APEX_EST_AX_SOL.MARCADA_AX = 'N') → POST /:id/integrar com o numero do
///      card → 'S'. Recusa definitiva (4xx) → 'E' + erro (nao fica tentando para sempre).
///   3. <b>saida</b> — APEX_EST_AX_SAIDA PENDENTE das solicitacoes confirmadas, na ordem → POST /:id/status.
///      Rede/5xx: para a rodada e tenta tudo de novo depois. 4xx: conta tentativa; apos MaxTentativas vira ERRO. Uma
///      linha travada segura as seguintes da MESMA solicitacao, para as fases nao chegarem fora de ordem.
/// </summary>
public sealed class EsteiraSync
{
    private readonly AceleraClient _acelera;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger _log;
    private readonly int _maxTentativas;

    public EsteiraSync(AceleraClient acelera, IDbConnectionFactory db, ILogger log, int maxTentativas)
    {
        _acelera = acelera;
        _db = db;
        _log = log;
        _maxTentativas = Math.Max(1, maxTentativas);
    }

    public async Task<RodadaEsteira> RodadaAsync(CancellationToken ct)
    {
        await using var conn = await _db.CreateOpenAsync(ct);
        var recebidas = await EntradaAsync(conn, ct);
        var confirmadas = await ConfirmarAsync(conn, ct);
        var (enviadas, comErro) = await SaidaAsync(conn, ct);
        return new RodadaEsteira(recebidas, confirmadas, enviadas, comErro);
    }

    // ───────────────────────── 1. entrada ─────────────────────────

    private async Task<int> EntradaAsync(OracleConnection conn, CancellationToken ct)
    {
        var lista = await _acelera.ListarSolicitacoesAsync(ct);
        var recebidas = 0;
        foreach (var s in lista)
        {
            ct.ThrowIfCancellationRequested();
            var p = new OracleDynamicParameters(new { psol = s.CodiSol });
            var jaTem = await conn.ExecuteScalarAsync<decimal>(@"
SELECT (SELECT COUNT(*) FROM APEX_EST_AX_ENTRADA WHERE AX_CODI_SOL = :psol)
     + (SELECT COUNT(*) FROM APEX_EST_AX_SOL WHERE AX_CODI_SOL = :psol) FROM DUAL", p);
            if (jaTem > 0) continue;   // recebida antes; a confirmacao (passo 2) e' quem tira da lista do AceleraX

            (string? Ncon, IReadOnlyList<SolicitacaoItem> Itens) sol;
            try { sol = await _acelera.ObterSolicitacaoAsync(s.CodiSol, ct); }
            catch (ApiException e) when (!e.Transitorio)
            {
                _log.LogWarning("Esteira: solicitacao {Sol} nao lida no AceleraX: {Erro}", s.CodiSol, e.Message);
                continue;
            }
            if (sol.Itens.Count == 0)
            {
                _log.LogWarning("Esteira: solicitacao {Sol} sem itens no AceleraX — ignorada.", s.CodiSol);
                continue;
            }

            await using var tx = conn.BeginTransaction();
            try
            {
                var seq = 0;
                foreach (var it in sol.Itens)
                    await conn.ExecuteAsync(@"
INSERT INTO APEX_EST_AX_ENTRADA
  (ENTRADA_ID, AX_CODI_SOL, AX_NCON, SEQ, ORIGEM_ERP_ID, PRODUTO, QTDE, TIPO_TRANSPORTE, TIPO_PRIORIDADE, DATA_PROGRAMADA, OBSERVACAO)
VALUES
  (APEX_EST_AX_ENTRADA_SEQ.NEXTVAL, :psol, :pncon, :pseq, :porigem, :pprod, :pqtde, :ptransp, :pprio, :pdt, :pobs)",
                        new OracleDynamicParameters(new
                        {
                            psol = s.CodiSol, pncon = Corta(sol.Ncon ?? s.NconSol, 40), pseq = ++seq, porigem = Corta(it.OrigemErpId, 60),
                            pprod = Corta(it.Produto, 200), pqtde = it.Qtde, ptransp = Corta(it.TipoTransporte, 10),
                            pprio = Corta(it.TipoPrioridade, 10), pdt = it.DataProgramada, pobs = Corta(it.Observacao, 1000),
                        }), tx);
                tx.Commit();
                recebidas++;
                _log.LogInformation("Esteira: solicitacao {Ncon} recebida ({Itens} item(ns)).", sol.Ncon ?? s.CodiSol, sol.Itens.Count);
            }
            catch (OracleException e) when (e.Number == 1)
            {
                tx.Rollback();   // outra rodada gravou ao mesmo tempo
            }
            catch { tx.Rollback(); throw; }
        }
        return recebidas;
    }

    // ───────────────────────── 2. confirmacao ─────────────────────────

    private async Task<int> ConfirmarAsync(OracleConnection conn, CancellationToken ct)
    {
        var pendentes = (await conn.QueryAsync(@"
SELECT A.AX_CODI_SOL, (SELECT MIN(C.NUMERO) FROM APEX_EST_CARD C WHERE C.AX_CODI_SOL = A.AX_CODI_SOL) NUMERO
  FROM APEX_EST_AX_SOL A WHERE A.MARCADA_AX = 'N'")).Select(r => Rows.D((object)r)).ToList();
        var feitas = 0;
        foreach (var d in pendentes)
        {
            ct.ThrowIfCancellationRequested();
            var sol = Rows.Str(d, "AX_CODI_SOL")!;
            var numero = Rows.Long(d, "NUMERO");
            try
            {
                await _acelera.MarcarSolicitacaoIntegradaAsync(sol, numero is null ? null : $"#{numero}", ct);
                await conn.ExecuteAsync("UPDATE APEX_EST_AX_SOL SET MARCADA_AX = 'S' WHERE AX_CODI_SOL = :psol",
                    new OracleDynamicParameters(new { psol = sol }));
                feitas++;
            }
            catch (ApiException e) when (!e.Transitorio)
            {
                await conn.ExecuteAsync("UPDATE APEX_EST_AX_SOL SET MARCADA_AX = 'E', ULTIMO_ERRO = :perro WHERE AX_CODI_SOL = :psol",
                    new OracleDynamicParameters(new { psol = sol, perro = Corta(e.Message, 1000) }));
                _log.LogWarning("Esteira: solicitacao {Sol} recusada ao confirmar: {Erro}", sol, e.Message);
            }
        }
        return feitas;
    }

    // ───────────────────────── 3. saida ─────────────────────────

    private async Task<(int Enviadas, int ComErro)> SaidaAsync(OracleConnection conn, CancellationToken ct)
    {
        var fila = (await conn.QueryAsync(@"
SELECT * FROM (
  SELECT S.SAIDA_ID, S.AX_CODI_SOL, S.FASE, S.MOTIVO, S.SETOR, S.TENTATIVAS
    FROM APEX_EST_AX_SAIDA S JOIN APEX_EST_AX_SOL A ON A.AX_CODI_SOL = S.AX_CODI_SOL
   WHERE S.STATUS = 'PENDENTE' AND A.MARCADA_AX = 'S'
   ORDER BY S.SAIDA_ID)
 WHERE ROWNUM <= 200")).Select(r => Rows.D((object)r)).ToList();

        var travadas = new HashSet<string>();
        int enviadas = 0, comErro = 0;
        foreach (var d in fila)
        {
            ct.ThrowIfCancellationRequested();
            var id = Rows.Long(d, "SAIDA_ID")!.Value;
            var sol = Rows.Str(d, "AX_CODI_SOL")!;
            if (travadas.Contains(sol)) continue;
            var fase = Rows.Int(d, "FASE")!.Value;
            try
            {
                await _acelera.EnviarFaseSolicitacaoAsync(sol, fase, Rows.Str(d, "MOTIVO"), Rows.Str(d, "SETOR"), ct);
                await conn.ExecuteAsync(@"
UPDATE APEX_EST_AX_SAIDA SET STATUS = 'ENVIADO', DT_ENVIO = SYSTIMESTAMP, TENTATIVAS = TENTATIVAS + 1, ULTIMO_ERRO = NULL WHERE SAIDA_ID = :pid",
                    new OracleDynamicParameters(new { pid = id }));
                await conn.ExecuteAsync("UPDATE APEX_EST_AX_SOL SET FASE_ENVIADA = :pfase, DT_ENVIO = SYSTIMESTAMP WHERE AX_CODI_SOL = :psol",
                    new OracleDynamicParameters(new { pfase = fase, psol = sol }));
                enviadas++;
            }
            catch (ApiException e)
            {
                travadas.Add(sol);
                var tent = (Rows.Int(d, "TENTATIVAS") ?? 0) + 1;
                var status = !e.Transitorio && tent >= _maxTentativas ? "ERRO" : "PENDENTE";
                await conn.ExecuteAsync("UPDATE APEX_EST_AX_SAIDA SET STATUS = :pst, TENTATIVAS = :ptent, ULTIMO_ERRO = :perro WHERE SAIDA_ID = :pid",
                    new OracleDynamicParameters(new { pid = id, pst = status, ptent = tent, perro = Corta(e.Message, 1000) }));
                if (e.Transitorio) throw;   // rede/5xx: o resto fica para a proxima rodada
                comErro++;
                _log.LogWarning("Esteira: fase {Fase} da solicitacao {Sol} recusada: {Erro}", fase, sol, e.Message);
            }
        }
        return (enviadas, comErro);
    }

    private static string? Corta(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }
}
