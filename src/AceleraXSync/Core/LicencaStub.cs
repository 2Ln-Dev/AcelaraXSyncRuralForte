// Compilado SO' com -p:SemLicenca=true (ver AceleraXSync.csproj): substitui o pacote
// 2LN.Licensing quando ele nao esta disponivel (feed 2ln-local ausente). Mesma API que
// os workers usam (LicenseState.Current + AddAppLicensing), sempre LICENCIADO.
// Pacote gerado assim NAO valida licenca — usar so' ate o pacote voltar ao build.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TwoLN.Licensing;

public sealed record LicenseStatus(bool Licenciado, string Motivo, string Fingerprint);

public sealed class LicenseState
{
    public LicenseStatus Current { get; } = new(true, "Sem validacao de licenca (build -SemLicenca)", "");
}

public static class LicencaStubExtensions
{
    public static IServiceCollection AddAppLicensing(this IServiceCollection services, IConfiguration _)
        => services.AddSingleton<LicenseState>();
}
