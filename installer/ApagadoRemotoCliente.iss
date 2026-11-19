[Setup]
AppName=ApagadoRemoto Cliente
AppVersion=1.3.0
AppPublisher=ApagadoRemoto
DefaultDirName={commonpf}\ApagadoRemoto\Cliente
DefaultGroupName=ApagadoRemoto
OutputDir=J:\German\ReactN\ApagadoRemoto\installer
OutputBaseFilename=ApagadoRemotoClienteSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
UninstallDisplayIcon={app}\Apagado Remoto.exe
MinVersion=6.1sp1

[Files]
Source: "J:\German\ReactN\ApagadoRemoto\publish\Cliente\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "J:\German\ReactN\ApagadoRemoto\installer\addfirewall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "J:\German\ReactN\ApagadoRemoto\installer\removefirewall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "J:\German\ReactN\ApagadoRemoto\installer\addtask.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "J:\German\ReactN\ApagadoRemoto\installer\removetask.ps1"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\APR.exe"
Type: files; Name: "{app}\APR.dll"
Type: files; Name: "{app}\APR.pdb"
Type: files; Name: "{app}\APR.runtimeconfig.json"
Type: files; Name: "{app}\APR.deps.json"
Type: files; Name: "{app}\Apagado.Cliente.exe"
Type: files; Name: "{app}\Apagado.Cliente.dll"
Type: files; Name: "{app}\Apagado.Cliente.pdb"
Type: files; Name: "{app}\Apagado.Cliente.runtimeconfig.json"
Type: files; Name: "{app}\Apagado.Cliente.deps.json"

[Dirs]
Name: "{commonappdata}\ApagadoRemoto"; Permissions: users-readexec

[Icons]
Name: "{commondesktop}\ApagadoRemoto Cliente"; Filename: "{app}\Apagado Remoto.exe"
Name: "{group}\ApagadoRemoto Cliente"; Filename: "{app}\Apagado Remoto.exe"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ApagadoRemotoCliente"; Flags: deletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\addfirewall.ps1"""; Flags: runhidden; StatusMsg: "Configurando firewall..."
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\addtask.ps1"""; Flags: runhidden; StatusMsg: "Creando tarea de inicio automatico..."

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\removetask.ps1"""; Flags: runhidden; RunOnceId: "QuitarTareaInicio"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\removefirewall.ps1"""; Flags: runhidden; RunOnceId: "QuitarReglaFirewall"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Detiene las tareas y el cliente para poder reemplazar el ejecutable en una actualizacion. }
  Exec('schtasks.exe', '/End /TN "ApagadoRemoto Cliente"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('schtasks.exe', '/End /TN "ApagadoRemoto Fondo Escritorio"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM "Apagado Remoto.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM APR.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM Apagado.Cliente.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
