#ifndef PilotVersion
  #define PilotVersion "0.9.1"
#endif
[Setup]
AppId={{D970FA65-0364-4F10-A6AA-D4302F31B607}
AppName=ZagoSheetsWin
UninstallDisplayName=ZagoSheetsWin
AppVersion={#PilotVersion}
AppPublisher=Zagotools
AppPublisherURL=https://github.com/zagozago/ZagoSheetsWin
AppSupportURL=https://github.com/zagozago/ZagoSheetsWin/issues
SetupIconFile=..\branding\zagosheetswin.ico
WizardImageFile=..\branding\wizard.bmp
WizardSmallImageFile=..\branding\wizard-small.bmp
WizardImageBackColor=$0B1007
DefaultDirName={localappdata}\Programs\SheetsWindows
DefaultGroupName=ZagoSheetsWin
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ZagoSheetsWin-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
InfoBeforeFile=..\branding\CREDITS.txt
UninstallDisplayIcon={app}\SheetsWindows.exe
CloseApplications=yes

[Files]
Source: "..\artifacts\SheetsWindows-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ZagoSheetsWin"; Filename: "{app}\SheetsWindows.exe"
Name: "{group}\Restaurar backups"; Filename: "{app}\SheetsWindows.exe"; Parameters: "--recovery"
Name: "{group}\Desinstalar ZagoSheetsWin"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\SheetsWindows.exe"; Description: "Configurar ZagoSheetsWin"; Flags: postinstall nowait skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion: String;
  InstalledPacked, PackagePacked: Int64;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D970FA65-0364-4F10-A6AA-D4302F31B607}_is1', 'DisplayVersion', InstalledVersion) then
  begin
    if not StrToVersion(InstalledVersion, InstalledPacked) then
      Result := 'Installed version is invalid. Repair the version record before updating.'
    else if StrToVersion('{#PilotVersion}', PackagePacked) then
      if ComparePackedVersion(InstalledPacked, PackagePacked) > 0 then
        Result := 'A newer version is installed. Downgrades require an explicit migration and are blocked.';
  end;
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

procedure InitializeWizard;
var
  Credit: TNewStaticText;
begin
  Credit := TNewStaticText.Create(WizardForm);
  Credit.Parent := WizardForm;
  Credit.Caption := 'Zagotools | Open in Google - Swati K (SwatiK425) | MIT';
  Credit.Left := ScaleX(8);
  Credit.Top := WizardForm.ClientHeight - ScaleY(31);
  Credit.Width := WizardForm.BackButton.Left - ScaleX(16);
  Credit.WordWrap := True;
  Credit.Height := ScaleY(24);
  Credit.Font.Size := 7;
end;
