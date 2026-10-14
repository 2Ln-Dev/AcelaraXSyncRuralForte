using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AceleraXSync.Core;

namespace AceleraXSync.Dll;

/// <summary>Resposta interpretada da DLL. Port de dll_bridge.RespostaDLL.</summary>
public sealed class RespostaDll
{
    public bool Sucesso { get; init; }
    public string? NumeroPedido { get; init; }   // numeroPedido do ERP -> numero_erp
    public string? Status { get; init; }          // Header.Status cru
    public string? Mensagem { get; init; }        // mensagem de erro (se houver)
    public string Bruto { get; init; } = "";      // resposta crua (log/diagnostico)
    public bool JaCadastrado { get; init; }        // RNRPO002: pedido ja existe no ERP
}

public sealed class DllException : Exception
{
    public DllException(string msg, Exception? inner = null) : base(msg, inner) { }
}

/// <summary>
/// Ponte com a DLL 32-bit AGBPedidoVenda.dll. Port de pedido_erp/dll_bridge.py.
///
/// Exporta (stdcall):  IncluiPedidoVendaAPI(pDoc: PAnsiChar): PAnsiChar
/// Recebe o JSON do pedido (ANSI/cp1252) e devolve
///   { "Header": { "Status": "..." }, "Body": "&lt;json-string&gt;" }
/// Sucesso = extrair numeroPedido &gt; 0 do Body.
///
/// So carrega em processo 32-bit (a DLL e' x86) â€” por isso o projeto e' PlatformTarget x86.
/// </summary>
public sealed class DllBridge : IDisposable
{
    private const string FuncIncluir = "IncluiPedidoVendaAPI";
    private const string FuncLiberar = "LiberaDLL";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr IncluirDelegate(byte[] pDoc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void LiberarDelegate();

    private readonly string _dllPath;
    private readonly string _agbDir;
    private readonly Encoding _encoding;
    private readonly string? _oracleClientDir;
    private readonly ILogger<DllBridge> _log;

    private IntPtr _handle = IntPtr.Zero;
    private IncluirDelegate? _incluir;

    public DllBridge(IOptions<DllOptions> opt, ILogger<DllBridge> log)
    {
        var o = opt.Value;
        // Caminhos relativos (ex.: "agb\AGBPedidoVenda.dll") sao ancorados na pasta do
        // EXE, nao no diretorio de trabalho: como Windows Service o CWD e' C:\Windows\
        // system32, e um Path.GetFullPath cru resolveria a DLL la (e falharia). Caminho
        // absoluto passa intacto pelo overload (basePath e' ignorado).
        var baseDir = AppContext.BaseDirectory;
        _dllPath = Path.GetFullPath(o.DllPath, baseDir);
        _agbDir = string.IsNullOrWhiteSpace(o.AgbDir)
            ? Path.GetDirectoryName(_dllPath)!
            : Path.GetFullPath(o.AgbDir, baseDir);
        _encoding = Encoding.GetEncoding(o.Encoding);
        _oracleClientDir = string.IsNullOrWhiteSpace(o.OracleClientDir) ? null : o.OracleClientDir;
        _log = log;
    }

    /// <summary>LoadLibrary + GetProcAddress. chdir para a pasta do AGB.ini (a DLL le
    /// o ini a partir do diretorio de trabalho), replicando o IntegracaoDLLTest.exe.</summary>
    public void Carregar()
    {
        if (!File.Exists(_dllPath))
            throw new DllException($"DLL nao encontrada: {_dllPath}");

        // As dependencias nativas da DLL (driver dbExpress tbodbxora + Oracle client
        // OCI/oraocci11/OraOCIICUS11/MSVCR71) sao carregadas por NOME. Rodando como
        // Windows Service o CWD e' C:\Windows\system32 e o PATH nao tem a pasta do ERP,
        // entao garantimos que elas sejam achadas na NOSSA pasta (agbDir) prependando-a
        // ao PATH do processo. E' o que resolve o "Unable to Load tbodbxora.dll" (CON_006):
        // tbodbxora carrega, mas SUAS dependencias (oci/...) so' existem nessa pasta.
        // Oracle client 32-bit do proprio ERP (ex.: C:\siagri\bin) — evita copiar oci/
        // oraocci11/tbodbxora para a agb\. Entra DEPOIS da agb\ (agb\ vence se tiver copia).
        if (!string.IsNullOrWhiteSpace(_oracleClientDir))
        {
            if (Directory.Exists(_oracleClientDir)) PrependAoPath(_oracleClientDir);
            else _log.LogWarning("Dll:OracleClientDir nao existe: {Dir} (a DLL so' acha o Oracle client se ele estiver na agb\\ ou no PATH)", _oracleClientDir);
        }
        PrependAoPath(_agbDir);

        if (Directory.Exists(_agbDir))
            Directory.SetCurrentDirectory(_agbDir);

        try
        {
            _handle = NativeLibrary.Load(_dllPath);
        }
        catch (Exception e)
        {
            // Num processo x64 a DLL 32-bit nao carrega. O projeto e' x86 justamente p/ isso.
            throw new DllException(
                $"Falha ao carregar a DLL ({e.Message}). Confirme que o processo e' x86 " +
                "e que o Oracle client 32-bit esta acessivel.", e);
        }

        if (!NativeLibrary.TryGetExport(_handle, FuncIncluir, out var fn))
            throw new DllException($"Funcao '{FuncIncluir}' nao exportada pela DLL");

        _incluir = Marshal.GetDelegateForFunctionPointer<IncluirDelegate>(fn);
        _log.LogInformation("DLL carregada: {Dll} (agbDir={AgbDir})", _dllPath, _agbDir);
    }

    /// <summary>Coloca <paramref name="dir"/> no inicio do PATH do processo (idempotente),
    /// p/ que as dependencias nativas da DLL sejam resolvidas a partir dela.</summary>
    private static void PrependAoPath(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.StartsWith(dir + ";", StringComparison.OrdinalIgnoreCase)) return;
        Environment.SetEnvironmentVariable("PATH", dir + ";" + path);
    }

    /// <summary>Serializa o payload (cp1252) e chama a DLL. Devolve RespostaDll.</summary>
    public RespostaDll Incluir(JsonObject payload)
    {
        if (_incluir is null)
            throw new DllException("DLL nao carregada â€” chame Carregar() antes de Incluir().");

        var texto = payload.ToJsonString();
        // PAnsiChar terminado em nulo, no codepage da DLL (cp1252).
        var bytes = _encoding.GetBytes(texto);
        var entrada = new byte[bytes.Length + 1];
        Array.Copy(bytes, entrada, bytes.Length);

        var ret = _incluir(entrada);
        var bruto = LerAnsi(ret);
        return ParseResposta(bruto);
    }

    /// <summary>Chama LiberaDLL() se existir e libera a biblioteca.</summary>
    public void Liberar()
    {
        if (_handle == IntPtr.Zero) return;
        if (NativeLibrary.TryGetExport(_handle, FuncLiberar, out var fn))
        {
            try { Marshal.GetDelegateForFunctionPointer<LiberarDelegate>(fn)(); }
            catch { /* best-effort */ }
        }
        NativeLibrary.Free(_handle);
        _handle = IntPtr.Zero;
        _incluir = null;
    }

    public void Dispose() => Liberar();

    // Le um PAnsiChar (bytes ate o nulo) no codepage da DLL.
    private string LerAnsi(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "";
        var len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) len++;
        if (len == 0) return "";
        var buf = new byte[len];
        Marshal.Copy(ptr, buf, 0, len);
        return _encoding.GetString(buf);
    }

    /// <summary>Interpreta a resposta crua. Sucesso = numeroPedido &gt; 0 no Body;
    /// senao extrai a mensagem de negocio. RNRPO002 => JaCadastrado. Port do parser Python.</summary>
    public static RespostaDll ParseResposta(string bruto)
    {
        JsonNode? env;
        try { env = string.IsNullOrEmpty(bruto) ? null : JsonNode.Parse(bruto); }
        catch (JsonException)
        {
            return new RespostaDll { Sucesso = false, Mensagem = "Resposta da DLL nao e' JSON valido", Bruto = bruto };
        }

        string? status = null;
        JsonNode? bodyRaw = null;
        if (env is JsonObject obj)
        {
            var header = obj["Header"] ?? obj["header"];
            status = header?["Status"]?.ToString() ?? header?["status"]?.ToString();
            bodyRaw = obj["Body"] ?? obj["body"];
        }

        var body = LoadsMaybe(bodyRaw);
        string? numero = body is JsonObject bo
            ? (bo["numeroPedido"]?.ToString() ?? bo["numeropedido"]?.ToString())
            : null;

        if (!string.IsNullOrEmpty(numero) && numero != "0" && Num(numero) > 0)
            return new RespostaDll { Sucesso = true, NumeroPedido = numero, Status = status, Bruto = bruto };

        string? msg = null;
        if (body is JsonObject bo2)
            msg = bo2["message"]?.ToString() ?? bo2["Message"]?.ToString();
        if (string.IsNullOrEmpty(msg))
            msg = (bodyRaw as JsonValue)?.ToString() ?? bruto;

        var jaCadastrado = !string.IsNullOrEmpty(msg) && msg.Contains("RNRPO002");
        return new RespostaDll { Sucesso = false, Status = status, Mensagem = msg, Bruto = bruto, JaCadastrado = jaCadastrado };
    }

    // Body pode vir como string-JSON ou ja' como objeto.
    private static JsonNode? LoadsMaybe(JsonNode? v)
    {
        if (v is JsonValue val && val.TryGetValue<string>(out var s))
        {
            try { return JsonNode.Parse(s); }
            catch (JsonException) { return v; }
        }
        return v;
    }

    private static double Num(string? v) => double.TryParse(v, System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0.0;
}
