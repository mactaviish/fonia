; Instalador do Fonia (Inno Setup 6, https://jrsoftware.org/isinfo.php; licença livre, também para uso comercial).
; O Inno Setup é só a ferramenta que monta o instalador: ele não vai dentro do programa.
;
; Como gerar (de dentro de installer\, depois de "pwsh tools/publish.ps1"):
;   iscc /DAppVersion=0.1.0 fonia.iss
; O resultado é installer\Output\fonia-setup-0.1.0.exe. Em /DPackageDir= vai outra pasta de publicação, se não for dist\fonia.
;
; Instala só para o usuário (sem pedir administrador) em %LOCALAPPDATA%\Programs\Fonia. Os modelos de voz (~800 MB) não vão
; dentro: o instalador oferece baixá-los no fim, com "fonia --download-models". Desinstalar remove o programa e deixa os modelos
; (%LOCALAPPDATA%\Fonia\models) e as configurações (%APPDATA%\Fonia) no lugar, que são do usuário.

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
Name: "{group}\{#AppName} - baixar modelos de voz"; Filename: "{cmd}"; Parameters: "/k """"{app}\{#AppExe}"" --download-models"""; WorkingDir: "{app}"; Comment: "Baixa os modelos de voz que faltam (~800 MB)"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"

[Run]
; Os modelos são grandes: o instalador pergunta, e baixar pode ser feito depois pelo atalho. waituntilterminated mantém o assistente
; na tela enquanto a janela do download trabalha.
Filename: "{app}\{#AppExe}"; Parameters: "--download-models"; Description: "Baixar agora os modelos de voz (~800 MB, é preciso internet)"; Flags: postinstall skipifsilent waituntilterminated
