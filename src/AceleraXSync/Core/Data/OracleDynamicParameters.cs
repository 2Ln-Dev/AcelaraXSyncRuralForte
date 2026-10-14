using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace AceleraXSync.Core.Data;

/// <summary>
/// Parametros para comandos Oracle via Dapper. Copiado do appagroinsumos-net.
///
/// Por que existe: o Oracle.ManagedDataAccess faz bind POSICIONAL por padrao, mas
/// o motor Python (cx_Oracle) usa binds NOMEADOS (:id, :dt, ...) e os MERGE repetem
/// o mesmo nome (:dt duas vezes). Este handler liga <c>BindByName</c> na abertura do
/// comando, entao a ordem nao importa â€” igual ao Python.
/// </summary>
public sealed class OracleDynamicParameters : SqlMapper.IDynamicParameters
{
    private readonly DynamicParameters _dyn;

    public OracleDynamicParameters() => _dyn = new DynamicParameters();

    public OracleDynamicParameters(object? template) => _dyn = new DynamicParameters(template);

    public void Add(string name, object? value) => _dyn.Add(name, value);

    public T Get<T>(string name) => _dyn.Get<T>(name);

    void SqlMapper.IDynamicParameters.AddParameters(IDbCommand command, SqlMapper.Identity identity)
    {
        if (command is OracleCommand oc)
            oc.BindByName = true;

        ((SqlMapper.IDynamicParameters)_dyn).AddParameters(command, identity);
    }
}
