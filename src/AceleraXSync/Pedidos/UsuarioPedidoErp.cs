using System.Globalization;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Logging;
using AceleraXSync.Core.Data;

namespace AceleraXSync.Pedidos;

/// <summary>
/// Grava o pedido no ERP com o usuario do VENDEDOR do pedido. A DLL usa
/// usuarioInclusaoCancelamento como PEDIDO.CODI_USU (sem ele: RUSU001 "Usuario nao
/// informado"), e o AceleraX manda fixo 'ADM' (ERP_CONST.usuarioIntegracao). Aqui o sync
/// troca pelo login do vendedor: CADUSU.CODI_USU com CODI_PES = codigoVendedor1 (o
/// PEDIDO.COD1_PES). Na Rural, o pedido digitado no ERP sai com o usuario do proprio
/// vendedor (8487 de 10243 nos ultimos 180 dias, out/2026), e CADUSU.CODI_PES e' unico.
///
/// Vendedor sem login no CADUSU: mantem o usuario que veio do AceleraX e so' avisa no log.
/// Erro de banco propaga: o pedido fica TRANSITORIO e nao vai para o ERP com o usuario errado.
/// </summary>
public sealed class UsuarioPedidoErp
{
    private const string Campo = "usuarioInclusaoCancelamento";

    private readonly IDbConnectionFactory _db;
    private readonly ILogger<UsuarioPedidoErp> _log;

    public UsuarioPedidoErp(IDbConnectionFactory db, ILogger<UsuarioPedidoErp> log)
    {
        _db = db;
        _log = log;
    }

    public async Task AplicarAsync(string codiPed, JsonObject payload, CancellationToken ct)
    {
        var atual = payload[Campo]?.ToString();
        var vendedor = Num(payload["codigoVendedor1"]);
        if (vendedor is null)
        {
            _log.LogWarning("pedido={CodiPed} sem codigoVendedor1: grava com o usuario do AceleraX ({Usuario})", codiPed, atual);
            return;
        }

        await using var conn = await _db.CreateOpenAsync(ct);
        var usuario = await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT CODI_USU FROM CADUSU WHERE CODI_PES = :vendedor ORDER BY CODI_USU",
            new { vendedor }, cancellationToken: ct));
        usuario = usuario?.Trim();

        if (string.IsNullOrEmpty(usuario))
        {
            _log.LogWarning("pedido={CodiPed} vendedor {Vendedor} sem usuario no CADUSU: grava com o usuario do AceleraX ({Usuario})",
                codiPed, vendedor, atual);
            return;
        }

        payload[Campo] = usuario;
        _log.LogInformation("pedido={CodiPed} usuario do ERP = {Usuario} (vendedor {Vendedor})", codiPed, usuario, vendedor);
    }

    private static long? Num(object? v) =>
        long.TryParse(v?.ToString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
}
