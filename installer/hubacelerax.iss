; ============================================================================
;  Hub AceleraX - Instalador (Inno Setup 6)
;  Instala o servico .NET (single-file self-contained, win-x86) como Windows
;  Service. x86 porque grava o pedido no ERP pela DLL AGBPedidoVenda (32-bit,
;  P/Invoke in-process); a carga de base roda no mesmo processo. Na 1a
;  instalacao pergunta a conexao Oracle + a API AceleraX + o cliente das consultas;
;  em upgrades preserva a config. Headless: sem porta/frontend/firewall.
;
;  NOME: o produto chama-se "Hub AceleraX"; o servico e o .exe usam "HubAceleraX".
;  NAO reaproveita o nome "HubPadrao"/"Hub2LN" (ja' existe esse servico de outro
;  produto na mesma maquina) - AppId e servico sao proprios.
;
;  O AGB.ini (aponta o Oracle do ERP do cliente) NAO acompanha o pacote - o
;  operador o coloca na pasta agb\ apos instalar (ver agb\LEIA-AGB.txt).
;
;  Gerado pelo build.ps1:  ISCC /DAppVersion=1.0.0 hubacelerax.iss
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Hub AceleraX"
#define ServiceName "HubAceleraX"
#define ExeName "HubAceleraX.exe"

[Setup]
AppId={{E6C1A9D4-7B52-4F38-9A2C-HUBACELERAX0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=2LN Consultoria
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename={#ServiceName}-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
; x86: o processo precisa ser 32-bit p/ carregar a DLL AGB (nao force 64-bit).
ArchitecturesAllowed=x86compatible x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#AppName} {#AppVersion}

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; Todo o resultado do dotnet publish (build\publish), inclusive a pasta agb\. As consultas
; SQL vao EMBUTIDAS no exe. O appsettings.Production.json e o AGB.ini NAO vem aqui.
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; Upgrade de quem tinha os nomes de assembly antigos (dois servicos separados).
Type: files; Name: "{app}\AceleraXBaseSync.exe"
Type: files; Name: "{app}\AceleraXPedidoErp.exe"
Type: files; Name: "{app}\BaseSync.Service.exe"
Type: files; Name: "{app}\PedidoErp.Service.exe"
; Consultas SQL soltas de versoes antigas (agora ficam embutidas no exe).
Type: filesandordirs; Name: "{app}\consultas"

[Code]
var
  Pg: TInputQueryWizardPage;
  IsUpgrade: Boolean;

function ConfigPath(): String;
begin
  Result := ExpandConstant('{app}\appsettings.Production.json');
end;

procedure InitializeWizard;
begin
  IsUpgrade := FileExists(ExpandConstant('{autopf}\{#AppName}\appsettings.Production.json'));

  Pg := CreateInputQueryPage(wpSelectDir,
    'Conexao e integracao',
    'Dados deste cliente',
    'Informe o acesso ao Oracle do ERP e a API do AceleraX. NAO e necessario informar ' +
    'senha do Oracle - o sistema recupera o acesso automaticamente. Em atualizacoes ' +
    'estes dados sao preservados. Lembre de colocar o AGB.ini do ERP na pasta agb\.');
  Pg.Add('Schema do ERP (ex.: RURALF):', False);
  Pg.Add('DSN Oracle (host:porta/servico, ex.: 10.0.0.10:1521/orcl):', False);
  Pg.Add('URL do AceleraX (ex.: https://cliente.vercel.app):', False);
  Pg.Add('X-Api-Key da integracao:', False);
  Pg.Add('Cliente das consultas (embutidas no sistema; ex.: ruralforte):', False);
  Pg.Add('Pasta do Oracle client 32-bit do ERP (oci.dll/tbodbxora.dll; ex.: C:\siagri\bin):', False);
  Pg.Values[4] := 'ruralforte';
  Pg.Values[5] := 'C:\siagri\bin';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := IsUpgrade and (PageID = Pg.ID);
end;

function JsonEscape(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

// Para/remonta o servico antigo (dois servicos separados, se existirem) - sem isto
// eles continuariam rodando em paralelo ao novo.
procedure MigrarServicosAntigos();
var rc: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop AceleraXBaseSync',  '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop AceleraXPedidoErp', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(2000);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete AceleraXBaseSync',  '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete AceleraXPedidoErp', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

procedure StopService();
var rc: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(2500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopService();
  Result := '';
end;

procedure WriteConfig();
var json, schema, dsn, apiUrl, apiKey, consultas, oracleClient: String;
begin
  if IsUpgrade then exit;
  schema    := Trim(Pg.Values[0]);
  dsn       := Trim(Pg.Values[1]);
  apiUrl    := Trim(Pg.Values[2]);
  apiKey    := Trim(Pg.Values[3]);
  consultas := Trim(Pg.Values[4]);
  oracleClient := Trim(Pg.Values[5]);
  json :=
    '{' + #13#10 +
    '  "Oracle": {' + #13#10 +
    '    "Dsn": "' + JsonEscape(dsn) + '",' + #13#10 +
    '    "Schema": "' + JsonEscape(schema) + '",' + #13#10 +
    '    "PoolMin": 2,' + #13#10 +
    '    "PoolMax": 10' + #13#10 +
    '  },' + #13#10 +
    '  "Acelera": {' + #13#10 +
    '    "ApiUrl": "' + JsonEscape(apiUrl) + '",' + #13#10 +
    '    "ApiKey": "' + JsonEscape(apiKey) + '",' + #13#10 +
    '    "TimeoutSeconds": 120' + #13#10 +
    '  },' + #13#10 +
    '  "Sync": {' + #13#10 +
    '    "ConsultasDir": "' + JsonEscape(consultas) + '",' + #13#10 +
    '    "RunOnce": false,' + #13#10 +
    '    "IntervalSeconds": 3600,' + #13#10 +
    '    "BatchSize": 1000' + #13#10 +
    '  },' + #13#10 +
    '  "Dll": {' + #13#10 +
    '    "OracleClientDir": "' + JsonEscape(oracleClient) + '"' + #13#10 +
    '  },' + #13#10 +
    '  "Pedido": {' + #13#10 +
    '    "RunOnce": false,' + #13#10 +
    '    "PollSeconds": 300' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(ConfigPath(), json, False);
end;

procedure InstallOrStartService();
var rc: Integer; bin: String;
begin
  bin := ExpandConstant('"{app}\{#ExeName}"');
  if not Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc) or (rc <> 0) then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'),
      'create {#ServiceName} binPath= ' + bin + ' start= auto DisplayName= "{#AppName}"',
      '', SW_HIDE, ewWaitUntilTerminated, rc);
    Exec(ExpandConstant('{sys}\sc.exe'),
      'description {#ServiceName} "{#AppName} - sincronizador ERP <-> AceleraX (.NET + DLL AGB)."',
      '', SW_HIDE, ewWaitUntilTerminated, rc);
    Exec(ExpandConstant('{sys}\sc.exe'),
      'failure {#ServiceName} reset= 60 actions= restart/5000/restart/5000/restart/5000',
      '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

// appsettings.Production.json tem a conexao e a chave da API: so' Administradores e SYSTEM
// (conta do servico) leem. Tira a heranca (Users/Everyone saem) e da' controle total aos dois.
// SIDs (544 = Administradores, 18 = SYSTEM) para funcionar em Windows de qualquer idioma.
procedure RestringirConfig();
var rc: Integer;
begin
  if FileExists(ConfigPath()) then
    Exec(ExpandConstant('{sys}\icacls.exe'),
      '"' + ConfigPath() + '" /inheritance:r /grant:r *S-1-5-32-544:F *S-1-5-18:F',
      '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteConfig();
    RestringirConfig();   // tambem em atualizacao (arquivo preservado)
    MigrarServicosAntigos();
    InstallOrStartService();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var rc: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Sleep(2000);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
end;
