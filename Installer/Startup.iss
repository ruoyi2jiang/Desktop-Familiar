#ifndef StartupRunKey
  #define StartupRunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif
#ifndef StartupDataRoot
  #define StartupDataRoot "{localappdata}\DesktopFamiliar"
#endif

[Dirs]
Name: "{#StartupDataRoot}\Library"; Flags: uninsneveruninstall

[Registry]
Root: HKCU; Subkey: "{#StartupRunKey}"; ValueName: "{#MyAppName}"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"""; Flags: createvalueifdoesntexist noerror; Check: IsFirstUse

[Code]
var
  FreshLibrary: Boolean;
  OtherStartupCommand: String;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Capture first use before [Dirs] creates the library, even during silent setup. }
  FreshLibrary := not DirExists(ExpandConstant('{#StartupDataRoot}\Library')) and
                  not FileExists(ExpandConstant('{#StartupDataRoot}\desktop-familiar.json'));
  Result := '';
end;

function IsFirstUse: Boolean;
begin
  Result := FreshLibrary;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  case CurUninstallStep of
    usUninstall:
      if RegQueryStringValue(HKCU, '{#StartupRunKey}', '{#MyAppName}', Command) then
      begin
        if CompareText(Command, ExpandConstant('"{app}\{#MyAppExeName}"')) = 0 then
          RegDeleteValue(HKCU, '{#StartupRunKey}', '{#MyAppName}')
        else
          OtherStartupCommand := Command;
      end;
    usPostUninstall:
      { Upgraded installations can have an unconditional delete-value record from v0.10.0. }
      if (OtherStartupCommand <> '') and not RegValueExists(HKCU, '{#StartupRunKey}', '{#MyAppName}') then
        if not RegWriteStringValue(HKCU, '{#StartupRunKey}', '{#MyAppName}', OtherStartupCommand) then
          Log('Could not restore the Windows sign-in command for the other copy.');
  end;
end;
