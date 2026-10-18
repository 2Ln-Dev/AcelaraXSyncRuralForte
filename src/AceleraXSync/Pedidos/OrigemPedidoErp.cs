using System.Globalization;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Logging;
using AceleraXSync.Core.Data;

namespace AceleraXSync.Pedidos;

/// <summary>
/// Tira do pedido gravado pela DLL a marca de origem "SiagriApi". A DLL grava
/// PEDIDO.ORIG_PED = 'S', e o dicionario do SIAGRI (RBCAMPO) define ORIG_PED como "Onde o
/// pedido foi gerado: M-Mobile, 1-AFV Manager, P-PR VENDA, S-SiagriApi". O pedido digitado
/// no ERP fica com ORIG_PED NULL (Rural, 397 de 397 em out/2026), e e' assim que o cliente
/// quer o pedido do AceleraX.
///
/// Chave do pedido: CODI_EMP + SERI_PED + PEDI_PED. Empresa e serie vem do payloadErp
/// (codigoEmpresa/seriePedido) e o numero, da resposta da DLL.
/// Melhor esforco: o pedido JA esta no ERP, entao falha aqui so' gera aviso no log e
/// nunca muda o desfecho do pedido (nem dispara nova gravacao).
/// </summary>
public sealed class OrigemPedidoErp
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<OrigemPedidoErp> _log;

    public OrigemPedidoErp(IDbConnectionFactory db, ILogger<OrigemPedidoErp> log)
    {
        _db = db;
        _log = log;
    }

    public async Task LimparAsync(string codiPed, JsonObject payload, string? numeroPedido, CancellationToken ct)
    {
        var emp = Num(payload["codigoEmpresa"]);
        var ped = Num(numeroPedido);
        var serie = payload["seriePedido"]?.ToString()?.Trim();
        if (emp is null || ped is null || string.IsNullOrEmpty(serie))
        {
            _log.LogWarning("pedido={CodiPed} ORIG_PED NAO limpo: chave incompleta (empresa={Emp} serie={Serie} numero={Numero})",
                codiPed, payload["codigoEmpresa"]?.ToString(), serie, numeroPedido);
            return;
        }

        try
        {
            await using var conn = await _db.CreateOpenAsync(ct);
            var linhas = await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE PEDIDO SET ORIG_PED = NULL " +
                "WHERE CODI_EMP = :emp AND SERI_PED = :serie AND PEDI_PED = :ped AND ORIG_PED IS NOT NULL",
                new { emp, serie, ped }, cancellationToken: ct));
            _log.LogInformation("pedido={CodiPed} ORIG_PED limpo no ERP (empresa={Emp} serie={Serie} numero={Numero} linhas={Linhas})",
                codiPed, emp, serie, ped, linhas);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.LogWarning(e, "pedido={CodiPed} gravado no ERP (empresa={Emp} serie={Serie} numero={Numero}) mas o ORIG_PED NAO foi limpo",
                codiPed, emp, serie, ped);
        }
    }

    private static long? Num(object? v) =>
        long.TryParse(v?.ToString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
}
