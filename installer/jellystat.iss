; Inno Setup script for running Jellystat natively on Windows as a service.
; Built by the "build-windows" job in .github/workflows/docker-publish.yml:
;   ISCC.exe /DAppVersion=2.0.0.0 /DPublishDir=..\publish installer\jellystat.iss
;
; On a fresh install the wizard asks for the PostgreSQL details and the HTTP port, generates a JWT_KEY,
; and writes them to {app}\jellystat.json (read by Program.cs). Upgrades keep the existing jellystat.json
; and skip those pages. Uninstalling removes the service but leaves jellystat.json and backups\ behind.

#ifndef AppVersion
  #define AppVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define ServiceName "Jellystat"
#define ServiceAccount "NT SERVICE\Jellystat"
#define AppExe "Jellystat-API-Core.exe"

[Setup]
AppId={{6F0C5B2E-8D41-4C7A-9B3E-2A1D7E5F9C04}
AppName=Jellystat
AppVersion={#AppVersion}
AppPublisher=Jellystat
AppPublisherURL=https://github.com/jellystat
DefaultDirName={autopf}\Jellystat
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=Jellystat-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "jellystat.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
; Delayed start gives PostgreSQL time to come up when it runs on the same machine.
; On upgrade the service already exists, so "create" fails harmlessly and the rest still applies.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\{#AppExe}\"""" start= delayed-auto obj= ""{#ServiceAccount}"" DisplayName= ""Jellystat"""; Flags: runhidden; StatusMsg: "Registering the Jellystat service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Jellystat statistics for Jellyfin and Emby"""; Flags: runhidden
; Restart after a crash, for example when the database was not reachable yet
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000"; Flags: runhidden
; The service account updates wwwroot\ (UI updates) and writes backups\, so it needs modify rights on the install folder
Filename: "{sys}\icacls.exe"; Parameters: """{app}"" /grant ""{#ServiceAccount}:(OI)(CI)M"""; Flags: runhidden
; jellystat.json holds the DB password and JWT key: Administrators, SYSTEM and the service account only
Filename: "{sys}\icacls.exe"; Parameters: """{app}\jellystat.json"" /inheritance:r /grant:r *S-1-5-32-544:F *S-1-5-18:F ""{#ServiceAccount}:R"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "Starting the Jellystat service..."
Filename: "http://localhost:{code:GetHttpPort}"; Description: "Open Jellystat in the browser"; Flags: shellexec postinstall skipifsilent nowait unchecked; Check: IsNewConfig

[Code]
var
  DatabasePage: TInputQueryWizardPage;
  ServerPage: TInputQueryWizardPage;
  NewConfig: Boolean;

function ConfigPath(): String;
begin
  Result := ExpandConstant('{app}\jellystat.json');
end;

function IsNewConfig(): Boolean;
begin
  Result := NewConfig;
end;

function GetHttpPort(Param: String): String;
begin
  Result := Trim(ServerPage.Values[0]);
end;

function IsPort(Value: String): Boolean;
var
  Port: Integer;
begin
  Port := StrToIntDef(Trim(Value), -1);
  Result := (Port > 0) and (Port < 65536);
end;

function JsonEscape(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure InitializeWizard();
begin
  DatabasePage := CreateInputQueryPage(wpSelectDir,
    'Database', 'PostgreSQL connection',
    'Jellystat stores its data in PostgreSQL. Enter the details of the server and an account that can create the database.');
  DatabasePage.Add('Host:', False);
  DatabasePage.Add('Port:', False);
  DatabasePage.Add('Database name:', False);
  DatabasePage.Add('Username:', False);
  DatabasePage.Add('Password:', True);
  DatabasePage.Values[0] := 'localhost';
  DatabasePage.Values[1] := '5432';
  DatabasePage.Values[2] := 'jellystat';
  DatabasePage.Values[3] := 'postgres';

  ServerPage := CreateInputQueryPage(DatabasePage.ID,
    'Web interface', 'Port for the Jellystat web interface',
    'Jellystat will be available in your browser at http://localhost:<port> (not the database port, that was on the previous page). ' +
    'Pick a port nothing else on this PC uses. Allow it through Windows Firewall if other devices need to reach Jellystat.');
  ServerPage.Add('Web interface port:', False);
  ServerPage.Values[0] := '3000';
end;

// Upgrades keep the existing settings, so the settings pages only show when there is no jellystat.json yet.
// Checked here rather than when leaving the folder page, because that page is skipped on upgrades.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = DatabasePage.ID) or (PageID = ServerPage.ID) then
  begin
    NewConfig := not FileExists(AddBackslash(WizardDirValue()) + 'jellystat.json');
    Result := not NewConfig;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = DatabasePage.ID then
  begin
    if (Trim(DatabasePage.Values[0]) = '') or (Trim(DatabasePage.Values[2]) = '') or
       (Trim(DatabasePage.Values[3]) = '') or (DatabasePage.Values[4] = '') then
    begin
      MsgBox('Host, database name, username and password are required.', mbError, MB_OK);
      Result := False;
    end
    else if not IsPort(DatabasePage.Values[1]) then
    begin
      MsgBox('The database port must be a number between 1 and 65535.', mbError, MB_OK);
      Result := False;
    end;
  end;

  if (CurPageID = ServerPage.ID) and not IsPort(ServerPage.Values[0]) then
  begin
    MsgBox('The HTTP port must be a number between 1 and 65535.', mbError, MB_OK);
    Result := False;
  end;
end;

// Pascal Script has no secure random source, so PowerShell generates the JWT signing key
function GenerateJwtKey(): String;
var
  KeyFile: String;
  Key: AnsiString;
  ResultCode: Integer;
begin
  Result := '';
  KeyFile := ExpandConstant('{tmp}\jwtkey.txt');
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$b = New-Object byte[] 48; ' +
    '[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); ' +
    '[IO.File]::WriteAllText(''' + KeyFile + ''', [Convert]::ToBase64String($b))"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and LoadStringFromFile(KeyFile, Key) then
    Result := Trim(String(Key));
  DeleteFile(KeyFile);
end;

procedure WriteConfig();
var
  JwtKey: String;
  Lines: TArrayOfString;
begin
  JwtKey := GenerateJwtKey();
  if Length(JwtKey) < 32 then
    MsgBox('Could not generate a JWT key. Jellystat will still start, but users will be signed out after every restart until JWT_KEY is set in ' + ConfigPath() + '.', mbInformation, MB_OK);

  SetArrayLength(Lines, 9);
  Lines[0] := '{';
  Lines[1] := '  "DB_HOST": "' + JsonEscape(Trim(DatabasePage.Values[0])) + '",';
  Lines[2] := '  "DB_PORT": "' + Trim(DatabasePage.Values[1]) + '",';
  Lines[3] := '  "DB_NAME": "' + JsonEscape(Trim(DatabasePage.Values[2])) + '",';
  Lines[4] := '  "DB_USERNAME": "' + JsonEscape(Trim(DatabasePage.Values[3])) + '",';
  Lines[5] := '  "DB_PASSWORD": "' + JsonEscape(DatabasePage.Values[4]) + '",';
  Lines[6] := '  "JWT_KEY": "' + JwtKey + '",';
  Lines[7] := '  "Urls": "http://*:' + Trim(ServerPage.Values[0]) + '"';
  Lines[8] := '}';
  SaveStringsToUTF8File(ConfigPath(), Lines, False);
end;

// sc query fails with 1060 once the service no longer exists
function ServiceStillListed(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Stops the service and makes sure its process is gone, so its files can be replaced or removed.
// The restart-on-failure actions are cleared first: otherwise a service stuck in a crash loop (e.g. database
// unreachable) can be started again by Windows mid-install. The [Run] section sets them again after installing.
procedure StopService();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceName} reset= 0 actions= ""', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopService();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopService();
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Windows only removes the service once nothing holds it open; the Services console is the usual culprit
    if not UninstallSilent() and ServiceStillListed() then
      MsgBox('The Jellystat service is marked for deletion. Close the Services console (services.msc) and Task Manager to finish removing it, or restart Windows.', mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Interactive installs only: a silent first install has no answers to write, so it needs a jellystat.json in place
  if (CurStep = ssPostInstall) and NewConfig and not WizardSilent() and not FileExists(ConfigPath()) then
    WriteConfig();
end;
