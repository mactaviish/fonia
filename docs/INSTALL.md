# Instalação e primeiro uso

O Fonia é um aplicativo de ATC por voz para o Microsoft Flight Simulator 2020: você fala no rádio, ele entende e responde com voz de controlador, usando a fraseologia do DECEA (MCA 100-16). Tudo roda no seu computador, sem conta e sem enviar a sua voz para lugar nenhum.

## O que você precisa

- **Windows 10 ou 11, 64 bits.**
- **Microsoft Flight Simulator 2020** (o 2024 e o X-Plane ainda não são suportados).
- **Um microfone** e um alto-falante ou fone.
- **Uns 1,2 GB livres** (o programa tem ~160 MB e os modelos de voz, ~800 MB) e internet **só** para baixar os modelos e, se quiser, o METAR do aeroporto.
- Os componentes **Microsoft Visual C++ 2015-2022 (x64)**, que o próprio MSFS já instala. Se o programa reclamar de `VCRUNTIME140.dll`, instale o "Visual C++ Redistributable" no site da Microsoft.
- Não precisa instalar o .NET: ele já vem junto.

## Instalar

1. Baixe `fonia-setup-<versão>.exe` e execute. Ele instala só para o seu usuário (sem pedir administrador) em `%LOCALAPPDATA%\Programs\Fonia` e cria os atalhos no menu Iniciar.
2. O Windows pode mostrar o aviso **"O Windows protegeu o computador"** (SmartScreen), porque o instalador ainda não é assinado digitalmente. Clique em **Mais informações** e depois em **Executar assim mesmo**. A soma de verificação (SHA-256) de cada arquivo está em `SHA256SUMS.txt` na página do lançamento.
3. Na página **"Selecionar tarefas adicionais"**, deixe marcados os dois **modelos de voz**: o reconhecimento de voz (Whisper small, 466 MB) e a voz do controlador (Kokoro-82M, 353 MB). **O Fonia só funciona com os dois.** Ao fim da instalação abre uma janela que baixa os marcados, confere a integridade (SHA-256) e guarda em `%LOCALAPPDATA%\Fonia\models`; ela mostra o resultado, inclusive "já estão instalados", e espera uma tecla antes de fechar. Se for interrompido, é só rodar de novo: continua de onde parou. Se você desmarcar algum, o Fonia pergunta se deve baixar na primeira vez em que for usado, e também dá para baixar depois pelo atalho **"Fonia - baixar modelos de voz"**.

*Sem o instalador:* descompacte o `.zip` numa pasta e rode `fonia.exe --download-models` uma vez (`--only stt` ou `--only tts` baixam só um deles).

## Primeiro uso

1. Abra o MSFS e **carregue um voo** parado em um aeroporto (por exemplo, no pátio de Curitiba, SBCT).
2. Abra o **Fonia** no menu Iniciar. Ele procura o simulador: com o voo carregado, pega sozinho o **indicativo** (a matrícula da aeronave) e o **aeroporto** onde você está, e carrega os modelos. Se o MSFS não estiver aberto (ou o voo ainda não carregou), ele avisa e **pergunta o aeroporto** (código ICAO, por exemplo `SBCT`) **e o indicativo** (por exemplo `PR-ABC`), sugerindo o que você usou da última vez. Assim dá para testar sem o simulador, só sem leitura do rádio COM1.
3. Quando aparecer *"Pronto. Segure RControl, fale e solte"*, **segure o Ctrl direito**, fale e solte. O controlador responde em voz alta e o texto do que ele entendeu aparece na janela.
4. **Trocar de voo não exige reiniciar o Fonia:** se você carregar outro voo no MSFS (outro aeroporto ou outra aeronave), ele percebe sozinho, mostra "Novo voo detectado" e recomeça com o aeroporto e o indicativo novos.
5. **Sintonize o rádio COM1 do MSFS** na frequência certa: quem responde depende dela (o programa mostra as frequências do aeroporto ao abrir). Solo para acionamento e táxi, Torre para decolar e pousar. Em uma frequência que não é do aeroporto, ninguém responde e o programa avisa.

Fale como numa conversa de rádio, com o **seu indicativo** (a matrícula que está no simulador, dita por extenso: "Papa Romeo Foxtrot Romeo Alfa") e os números dígito a dígito ("pista uno cinco"). Um voo completo:

| Você diz | O controlador |
|---|---|
| "Solo Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicito acionamento" | "…, acionamento aprovado, chame pronto para o táxi." |
| "…, pronto para o táxi" | "…, autorizado táxi para o ponto de espera da pista uno cinco…" |
| (você repete o que ele disse, com o indicativo) | fica em silêncio quando está certo; diz "negativo" e a versão certa quando não está |
| "…, no ponto de espera da pista uno cinco" | "…, chame Torre Curitiba…" (troque o COM1 para a Torre) |
| "Torre…, pronto para decolagem" / "alinhado" | "alinhe e mantenha" / "autorizado decolagem" |
| (decola) | a Torre diz sozinha "decolado aos…" |
| "…, instruções para pouso", "perna do vento", "perna base", "final" | circuito e autorização de pouso |
| (pousa) | a Torre diz sozinha "no solo aos…, chame Solo…" |

Se pedir "informações de partida" ou "condições do aeródromo", ele diz pista em uso, vento, altímetro, temperatura, visibilidade e a hora certa (com o METAR real do aeroporto, quando há internet).

## Configurar

As suas preferências ficam em `%APPDATA%\Fonia\appsettings.json`: tecla do push-to-talk, microfone, voz do controlador, efeito de rádio, uso da GPU no reconhecimento, aeroporto sugerido, METAR ligado ou não. O atalho **"Fonia - configurações"** cria o arquivo (com tudo comentado, para você editar no bloco de notas) e mostra o que está valendo; é o mesmo que `fonia --init-config` e `fonia --config`. O que você digita na linha de comando sempre vale mais do que o arquivo. A lista completa de opções está em `fonia --help`.

Duas que costumam ajudar:

- **`"sttGpu": true`**: reconhece a fala na placa de vídeo, ~0,25 s em vez de ~2 s. Usa a GPU do simulador por instantes; se o MSFS ficar pesado, desligue.
- **`"pttKey": "F8"`** (ou `Mouse4`, `CapsLock`...): troca a tecla de falar.

Para ver os microfones e alto-falantes e escolher outro: `fonia --mic-list`, depois `micDevice` e `audioOut`.

## Se algo não funcionar

| Sintoma | O que fazer |
|---|---|
| "Ninguém responde em 124.85…" | O COM1 do simulador não está na frequência do aeroporto. Sintonize a que o programa mostrou ao abrir. |
| "repita" com frequência | Fale mais perto do microfone, sem pressa, com o indicativo no começo ou no fim. `fonia --mic-test teste.wav` grava um trecho para você ouvir. |
| O indicativo não é reconhecido | O programa lê a matrícula do simulador. Confira em `fonia --config` e, se precisar, fixe `"callsign"` no arquivo. |
| Voo de companhia virtual (WVR1234) soletrado em vez do nome | O Fonia só conhece as companhias da tabela dele. Diga o nome com `--telephony "Wave Air"` (ou `"telephony": { "WVR": "Wave Air" }` no arquivo) e a matrícula com `--registration PR-ABC`. |
| "não consegui o indicativo da aeronave no simulador" | O MSFS precisa estar aberto com um voo **carregado**, ou use `--callsign PR-ABC`. |
| Reclama de modelo faltando | Rode `fonia --download-models`. |
| Sem METAR | Sem internet o controlador só deixa de dizer vento e altímetro. `--no-metar` evita a tentativa. |
| Janela fecha logo | Use o atalho do menu Iniciar (ele deixa a janela aberta) ou abra pelo terminal para ver a mensagem. |

Os registros mais detalhados saem com `--log-level information` (ou `debug`).

## O que o Fonia guarda sobre os seus voos

Para melhorar, o Fonia guarda **no seu computador** (nunca envia) as trocas em que o controlador não entendeu: o texto que o reconhecedor escreveu, em que fase do voo foi e o que o controlador respondeu, em `%LOCALAPPDATA%\Fonia\cases`. **O som da sua voz não é guardado**, a não ser que você ligue `--save-audio` (ou `"saveAudio": true` no arquivo de configuração). Quando o controlador entender errado sem saber (por exemplo, autorizar o que você não pediu), aperte **ScrollLock** para marcar a troca; no modo texto, digite `/errado`. `fonia --cases` mostra a pasta e quantos casos há. Os casos com mais de 30 dias são apagados sozinhos, e a pasta não passa de 200 MB. Para desligar, `--no-cases` (ou `"save": false` na seção `cases`). Se um dia você quiser nos ajudar, o conteúdo dessa pasta é o que você poderia mandar; hoje não existe envio automático.

## Desinstalar

Pelo menu Iniciar (**Desinstalar Fonia**) ou em *Configurações > Aplicativos*. O programa sai sempre (o .NET e as bibliotecas vão dentro dele, então não sobra dependência). O desinstalador **pergunta** se você quer remover também o que o Fonia guardou para você, e a resposta padrão é **Não**:

| O que é | Onde fica |
|---|---|
| os modelos de voz baixados (~850 MB) | `%LOCALAPPDATA%\Fonia\models` |
| o METAR guardado e as últimas respostas (aeroporto e indicativo) | `%LOCALAPPDATA%\Fonia\cache` e `last-*.txt`, na mesma pasta |
| os casos em que o controlador não entendeu (e o som, se você deixou) | `%LOCALAPPDATA%\Fonia\cases` |
| as suas configurações | `%APPDATA%\Fonia\appsettings.json` |

Com **Sim**, as duas pastas `Fonia` (a de `%LOCALAPPDATA%` e a de `%APPDATA%`) são apagadas por inteiro. Com **Não**, ficam, e um Fonia instalado depois as reaproveita sem baixar tudo de novo. Se você configurou outra pasta para os modelos (`modelsDir`), ela não é apagada: remova à mão. O programa não grava arquivos de registro (os logs saem na janela).

Em um desinstalar silencioso (`/SILENT`, `/VERYSILENT`) nada do usuário é apagado, a não ser que se acrescente `/PURGEDATA`.

## Para quem gera o pacote

`pwsh tools/publish.ps1` publica a pasta autossuficiente e o `.zip` em `dist\`; `iscc /DAppVersion=<versão> installer\fonia.iss` (Inno Setup 6) gera o instalador. O workflow `Release (Windows)` faz os dois quando se cria uma tag `v<versão>` (a mesma de `<Version>` em `Directory.Build.props`) e deixa um **rascunho** de lançamento com os arquivos e as somas de verificação, para revisar antes de publicar.
