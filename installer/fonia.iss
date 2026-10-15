; Instalador do Fonia (Inno Setup 6, https://jrsoftware.org/isinfo.php; licença livre, também para uso comercial).
; O Inno Setup é só a ferramenta que monta o instalador: ele não vai dentro do programa.
;
; Como gerar (de dentro de installer\, depois de "pwsh tools/publish.ps1"):
;   iscc /DAppVersion=0.1.0 fonia.iss
; O resultado é installer\Output\fonia-setup-0.1.0.exe. Em /DPackageDir= vai outra pasta de publicação, se não for dist\fonia.
;
; Instala só para o usuário (sem pedir administrador) em %LOCALAPPDATA%\Programs\Fonia. Os modelos de voz (~850 MB) não vão
; dentro: o instalador mostra uma caixa de seleção para cada um (marcados, com o aviso de que os dois são necessários) e baixa os
; marcados com "fonia --download-models" numa janela que mostra o resultado. Desinstalar remove o programa e PERGUNTA se deve
; remover também o que é do usuário: os modelos, o cache e as lembranças (%LOCALAPPDATA%\Fonia) e as configurações (%APPDATA%\Fonia).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PackageDir
  #define PackageDir "..\dist\fonia"
#endif

#define AppName "Fonia"
#define AppExe "fonia.exe"

[Setup]
; Identifica o Fonia para as atualizações e a desinstalação: nunca mude este valor.
AppId={{6F1D4C0B-5A27-4E9A-9C1E-3B7D2A8E41F6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Leonardo Yuri Dalla Corte Brancalione
AppCopyright=Copyright 2026 Leonardo Yuri Dalla Corte Brancalione
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=fonia-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExe}
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; O programa é de console: sem argumentos ele pergunta o aeroporto e conversa por voz. "cmd /k" mantém a janela aberta se algo falhar.
Name: "{group}\{#AppName}"; Filename: "{cmd}"; Parameters: "/k """"{app}\{#AppExe}"""""; WorkingDir: "{app}"; Comment: "Conversar por voz com o controle de tráfego aéreo"
Name: "{group}\{#AppName} - configurações"; Filename: "{cmd}"; Parameters: "/k """"{app}\{#AppExe}"" --init-config & ""{app}\{#AppExe}"" --config"""; WorkingDir: "{app}"; Comment: "Cria e mostra o arquivo de configuração"
Name: "{group}\{#AppName} - baixar modelos de voz"; Filename: "{cmd}"; Parameters: "/k """"{app}\{#AppExe}"" --download-models"""; WorkingDir: "{app}"; Comment: "Baixa os modelos de voz que faltam (~850 MB)"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"

[Tasks]
; A página "Selecionar tarefas adicionais" do instalador. Os modelos são grandes e não vão dentro do instalador: são baixados dos donos
; (Hugging Face e GitHub), conferidos pelo SHA-256 e guardados em %LOCALAPPDATA%\Fonia\models. O programa só funciona com os dois.
Name: "models_stt"; Description: "Reconhecimento de voz: Whisper small (466 MB)"; GroupDescription: "Modelos de voz (~850 MB, é preciso internet). O Fonia só funciona com os dois: se você desmarcar algum, ele pergunta se deve baixar na primeira vez que for usado."; Flags: checkedonce
Name: "models_tts"; Description: "Voz do controlador: Kokoro-82M (353 MB)"; Flags: checkedonce

[Run]
; Uma janela mostra o progresso e, no fim, o resultado (inclusive "já estão instalados"), e espera uma tecla: sem isso ela fecharia na hora.
; Pode ser interrompida e refeita: continua de onde parou. Depois da instalação, o atalho "baixar modelos de voz" faz o mesmo.
Filename: "{cmd}"; Parameters: "{code:DownloadParameters}"; StatusMsg: "Baixando os modelos de voz..."; Flags: skipifsilent waituntilterminated; Check: AnyModelSelected

[Code]
function AnyModelSelected: Boolean;
begin
  Result := WizardIsTaskSelected('models_stt') or WizardIsTaskSelected('models_tts');
end;

// cmd /c ""fonia.exe" --download-models [--only stt|tts] & echo. & pause": o cmd tira a primeira e a última aspas.
function DownloadParameters(Param: String): String;
var
  Only: String;
begin
  if WizardIsTaskSelected('models_stt') and WizardIsTaskSelected('models_tts') then
    Only := ''
  else if WizardIsTaskSelected('models_stt') then
    Only := ' --only stt'
  else
    Only := ' --only tts';

  Result := '/c ""' + ExpandConstant('{app}\{#AppExe}') + '" --download-models' + Only + ' & echo. & pause"';
end;

// ---------------------------------------------------------------------------------------------------------------------------------
// Desinstalar: o programa sai sempre; o que é do usuário (modelos, configurações, cache) só sai se ele disser que sim.
// Silencioso (/SILENT, /VERYSILENT) não apaga nada, a não ser que se passe /PURGEDATA.

var
  RemoveUserData: Boolean;

function PurgeRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/PURGEDATA') = 0 then
      Result := True;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;

  if PurgeRequested then
    RemoveUserData := True
  else if not UninstallSilent then
    RemoveUserData := MsgBox(
      'Remover também tudo o que o Fonia guardou para você?' + #13#10 + #13#10 +
      '  - os modelos de voz baixados (~850 MB)' + #13#10 +
      '  - as suas configurações (appsettings.json)' + #13#10 +
      '  - o METAR guardado e as últimas respostas (aeroporto e indicativo)' + #13#10 + #13#10 +
      'Se você escolher "Não", esses arquivos ficam e o próximo Fonia que você instalar os reaproveita, sem baixar de novo.' + #13#10 +
      'Se você configurou outra pasta para os modelos (modelsDir), ela não é apagada.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    DelTree(ExpandConstant('{localappdata}\Fonia'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\Fonia'), True, True, True);
  end;
end;
