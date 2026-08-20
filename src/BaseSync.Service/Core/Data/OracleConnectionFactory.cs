using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using BaseSync.Service.Core.Security;

namespace BaseSync.Service.Core.Data;

/// <summary>
/// Cria conexoes Oracle (pool do proprio driver via connection string). Adaptado do
/// HubPadrao (2Ln-Dev/HubPadrao) para o padrao "modo ponte":
///  - DIRETO (dev): appsettings traz Oracle:User + Password (ou PasswordEnc). Conecta
///    direto. E' o modo usado em Debug/desenvolvimento (e o que foi testado end-to-end).
///  - PONTE (cliente/producao): appsettings tem SO' Oracle:Schema + Dsn â€” NENHUMA senha.
///    Conecta com um USUARIO-PONTE fixo (cifrado no binario, ver <see cref="BridgeCredentials"/>),
///    le SELECT SENH_USU FROM &lt;schema&gt;.CADUSU WHERE CODI_USU=&lt;schema&gt;, decifra com
///    <see cref="ErpCrypto.Desencrypta"/> e abre a conexao DEFINITIVA como o proprio schema.
///    A senha do schema nunca fica em arquivo â€” vem do proprio ERP.
/// </summary>
public interface IDbConnectionFactory
{
    OracleConnection Create();
    Task<OracleConnection> CreateOpenAsync(CancellationToken ct = default);
}

public sealed class OracleConnectionFactory : IDbConnectionFactory
{
    private readonly OracleOptions _o;
    private readonly string? _schema;   // identificador Oracle validado (UPPER)
    private readonly bool _modoPonte;   // true = recupera a senha do schema via CADUSU

    private readonly object _lock = new();
    private string? _connString;        // conexao definitiva (so' em memoria)

    public OracleConnectionFactory(IOptions<OracleOptions> opt)
    {
        _o = opt.Value;
        _schema = NormalizarSchema(_o.Schema);

        // Sem Oracle:User => modo PONTE (senha do schema vem do CADUSU). Com User => modo
        // DIRETO (dev), resolve a connection string ja' aqui.
        _modoPonte = string.IsNullOrWhiteSpace(_o.User);
        if (_modoPonte)
        {
            if (_schema is null)
                throw new InvalidOperationException(
                    "Oracle:Schema e' obrigatorio no modo usuario-ponte (sem Oracle:User).");
        }
        else
        {
            _connString = MontarConnString(_o.User, SecretProtector.ResolverSenhaOracle(_o.Password, _o.PasswordEnc));
        }
    }

    private string MontarConnString(string user, string senha) => new OracleConnectionStringBuilder
    {
        UserID = user,
        Password = senha,
        DataSource = _o.Dsn,
        Pooling = true,
        MinPoolSize = _o.PoolMin,
        MaxPoolSize = _o.PoolMax,
    }.ConnectionString;

    // Whitelist de identificador Oracle â€” o schema vem de config e e' interpolado no ALTER
    // SESSION e no FROM do CADUSU, entao validamos p/ evitar injecao. UPPER p/ casar com
    // CODI_USU (o Delphi usa UpperCase). Vazio/nulo => sem schema.
    private static string? NormalizarSchema(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (!Regex.IsMatch(s, "^[A-Za-z][A-Za-z0-9_$#]*$"))
            throw new InvalidOperationException(
                $"Oracle:Schema invalido: '{s}'. Use um identificador Oracle (letras/digitos/_ $ #).");
        return s.ToUpperInvariant();
    }

    /// <summary>Resolve (uma vez, sob lock) a connection string definitiva. No modo ponte:
    /// conecta com o usuario-ponte, le a senha do schema no CADUSU e a decifra. As credenciais
    /// da ponte sao decifradas SO' aqui, em variavel local, e descartadas ao sair.</summary>
    private string ConnString()
    {
        if (_connString is not null) return _connString;
        lock (_lock)
        {
            _connString ??= ResolverViaPonte();
            return _connString;
        }
    }

    private string ResolverViaPonte()
    {
        var schema = _schema!; // ja' validado/UPPER no ctor

        // Connection string da PONTE em variavel local (credenciais decifradas so' aqui).
        string bootstrap;
        {
            var (usuario, senha) = BridgeCredentials.Abrir();
            bootstrap = new OracleConnectionStringBuilder
            {
                UserID = usuario,
                Password = senha,
                DataSource = _o.Dsn,
                Pooling = false, // conexao efemera, so' para o lookup
            }.ConnectionString;
            // usuario/senha saem de escopo aqui; nao sao guardados em lugar nenhum.
        }

        string senhaSchema;
        try
        {
            using var c = new OracleConnection(bootstrap);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = $"SELECT SENH_USU FROM {schema}.CADUSU WHERE CODI_USU = :codi";
            var p = cmd.CreateParameter();
            p.ParameterName = "codi";
            p.Value = schema;
            cmd.Parameters.Add(p);

            var res = cmd.ExecuteScalar();
            if (res is null or DBNull)
                throw new InvalidOperationException(
                    $"Usuario-ponte: schema '{schema}' nao encontrado em {schema}.CADUSU.");

            senhaSchema = ErpCrypto.Desencrypta(Convert.ToString(res));
        }
        finally
        {
            bootstrap = string.Empty; // solta a referencia a connection string da ponte
        }

        // Conexao DEFINITIVA como o proprio schema. A senha recuperada fica so' aqui, em
        // memoria (o pool do driver precisa dela) â€” nunca em arquivo/log.
        return MontarConnString(schema, senhaSchema);
    }

    // Toda conexao nova aplica CURRENT_SCHEMA na abertura (inclui reabertura de pool). No
    // modo ponte o login ja' E' o schema, entao isto e' redundante e inofensivo.
    private OracleConnection NovaConexao()
    {
        var c = new OracleConnection(ConnString());
        if (_schema is not null)
            c.StateChange += (_, e) =>
            {
                if (e.CurrentState != ConnectionState.Open) return;
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"ALTER SESSION SET CURRENT_SCHEMA = {_schema}";
                cmd.ExecuteNonQuery();
            };
        return c;
    }

    public OracleConnection Create() => NovaConexao();

    public async Task<OracleConnection> CreateOpenAsync(CancellationToken ct = default)
    {
        var c = NovaConexao();
        await c.OpenAsync(ct);
        return c;
    }
}
