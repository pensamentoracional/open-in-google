[Setup]
AppId={{D970FA65-0364-4F10-A6AA-D4302F31B607}
AppName=Sheets Windows
AppVersion=0.6.0
AppPublisher=pensamentoracional
DefaultDirName={localappdata}\Programs\SheetsWindows
DefaultGroupName=Sheets Windows
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=SheetsWindows-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\SheetsWindows.exe
CloseApplications=yes

[Files]
Source: "..\artifacts\SheetsWindows-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Sheets Windows"; Filename: "{app}\SheetsWindows.exe"
Name: "{group}\Restaurar backups"; Filename: "{app}\SheetsWindows.exe"; Parameters: "--recovery"
Name: "{group}\Desinstalar Sheets Windows"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\SheetsWindows.exe"; Description: "Configurar Sheets Windows"; Flags: postinstall nowait skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(RemoveBackslashUnlessRoot(WizardDirValue),
    ExpandConstant('{localappdata}\Programs\SheetsWindows')) <> 0 then
    Result := 'Use the dedicated per-user installation directory. Other directories are not supported.';
end;

procedure MaintainAssociation(const Argument: String);
var
  ExitCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\SheetsWindows.exe'), Argument, '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) then
    RaiseException('Could not run association maintenance. Existing data was preserved.');
  if ExitCode <> 0 then
    RaiseException('Association conflict. Remove the prior portable registration before installing, or repair this installation before uninstalling. Existing data was preserved.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then MaintainAssociation('--register');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then MaintainAssociation('--unregister');
end;
// No UninstallDelete: state, OAuth, backups, user .url and Google files are never installed here.
