# Fonia

**ATC por voz com IA para simuladores de voo, em português do Brasil.**

Fonia (Fonia.IA) é um aplicativo que roda ao lado do simulador e permite que você converse com controladores de tráfego aéreo usando a própria voz, no rádio, com fraseologia correta. Você fala naturalmente, o app entende o que você pretende fazer e responde como um controlador responderia.

> **Status: em desenvolvimento inicial.** Ainda não há ATC por voz. Hoje o app lê o estado da aeronave no Microsoft Flight Simulator 2020 em tempo real, reconecta sozinho se o simulador fechar e abrir, e grava e reproduz sessões (marcos M0 e M1). Também há o **ATC por texto** (M2): você digita a fala do piloto e o Solo de qualquer aeroporto do Brasil responde, com partida, pushback e táxi, cotejando o readback (veja "Desenvolvimento" abaixo). Este README descreve o objetivo e o rumo do projeto. Acompanhe o progresso no [roadmap](docs/ROADMAP.md).

## Por que o Fonia

Existem soluções de ATC com IA para simuladores, mas poucas pensadas para a fraseologia e o idioma de quem voa no Brasil. O Fonia nasce para preencher essa lacuna:

- **Português do Brasil primeiro**, seguindo a fraseologia DECEA/ICAO, com inglês (FAA/ICAO) depois.
- **Previsível**: a IA interpreta o que você disse, mas quem decide a resposta de ATC é um motor de regras determinístico. Isso evita instruções absurdas e mantém a resposta rápida.
- **Offline-first**: reconhecimento e síntese de voz locais, sem depender de serviços na nuvem.

## Como funciona

```
Voz (PTT) → Reconhecimento de fala → Interpretação (intenção) → Motor de ATC → Fraseologia → Síntese de voz → Áudio
                                                                      ↑
                                                         Telemetria do simulador
```

1. Você aperta o push-to-talk e fala: *"Solo Curitiba, PR-ABC, pátio 1, posição 3, solicita instruções de táxi"*. Não precisa decorar o manual: "pronto para o táxi" ou "solicito táxi" também funcionam.
2. O app transcreve a fala e extrai a intenção (solicitação de táxi, matrícula PR-ABC, posição 3).
3. O motor de ATC consulta o estado da aeronave e do aeroporto e decide a instrução.
4. A fraseologia monta a resposta, no padrão do MCA 100-16 do DECEA, e a voz do controlador fala de volta: *"PR-ABC, autorizado táxi para o ponto de espera da pista 15"*.

Mais detalhes em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Simuladores

| Simulador | Status |
|---|---|
| Microsoft Flight Simulator 2020 | Alvo do MVP |
| Microsoft Flight Simulator 2024 | Planejado |
| X-Plane 11 / 12 | Planejado |

## Funcionalidades planejadas

- Solo por voz (por texto já funciona: partida, pushback e táxi) e Torre (decolagem, pouso)
- ATIS com METAR real
- Transferência de frequência conforme o rádio do simulador
- Aproximação e Centro
- Tráfego de IA conversando no rádio
- Integração com plano de voo (SimBrief)
- Interface gráfica

A ordem e o detalhamento estão no [roadmap](docs/ROADMAP.md).

## Tecnologia

- **C# / .NET 10** (solução multi-projeto, com o núcleo independente de sistema operacional e simulador)
- **SimConnect** para o MSFS
- Reconhecimento e síntese de voz locais (opções em avaliação)
- Dados abertos de aeroportos (OurAirports)

## Requisitos (previstos)

- Windows 10/11
- Microsoft Flight Simulator 2020 (para o MVP)
- Microfone com push-to-talk
- .NET 10 SDK (para desenvolvimento)

## Estrutura do repositório

```
Fonia.sln
src/
  Fonia.Core/        # modelos, NLU, motor de ATC e fraseologia
  Fonia.Data/        # aeroportos e METAR
  Fonia.Sim.Mock/    # simulador falso para testes
  Fonia.Sim.Msfs/    # integração com o MSFS (SimConnect)
  Fonia.Audio/       # captura e reprodução de áudio
  Fonia.Speech/      # reconhecimento e síntese de voz
  Fonia.App/         # aplicativo
tests/
docs/
```

## Desenvolvimento

Pré-requisito: [.NET SDK](https://dotnet.microsoft.com/download) instalado.

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
```

O núcleo e os testes rodam sem simulador, usando o `MockSimAdapter`. Apenas a integração com o MSFS exige Windows e o simulador aberto.

Para ver o estado da aeronave em tempo real (com o MSFS 2020 aberto e um voo carregado):

```bash
dotnet run --project src/Fonia.App -- --sim msfs         # lê o MSFS (sem argumentos, num terminal, ele pergunta o aeroporto)
dotnet run --project src/Fonia.App -- --sim mock   # taxi simulado, sem simulador
dotnet run --project src/Fonia.App -- --help       # todas as opções (gravar e reproduzir sessões)
```

Para conversar com o controlador por texto (ATC do M2, sem áudio): você digita a fala do piloto e o Solo, a Torre ou o AFIS do aeroporto responde.

**Configuração:** as preferências (tecla do PTT, microfone, voz do controlador, GPU, aeroporto sugerido...) ficam em `%APPDATA%\Fonia\appsettings.json`. `fonia --init-config` cria o arquivo com tudo comentado e `fonia --config` mostra o que está valendo; o que você digita na linha de comando sempre vale mais. Sem argumentos, num terminal, `fonia` pergunta o aeroporto e conversa por voz (o antigo modo de ler o MSFS é `fonia --sim msfs`).

```bash
dotnet run --project src/Fonia.App -- --atc SBCT --callsign PR-ABC          # sem simulador
dotnet run --project src/Fonia.App -- --atc SBCT --runway 15                # indicativo da matrícula no MSFS
dotnet run --project src/Fonia.App -- --atc SBCT --callsign PR-ABC --com1 118.55   # rádio na frequência da Torre de Curitiba
```

Quem responde depende da **frequência do rádio**: o Solo na dele, a Torre na dela, ninguém em outra. Com o simulador (`--sim msfs`) ela vem do COM1 ao vivo; sem ele, use `--com1 <MHz>` ou digite `/com1 118.55` durante a sessão. O voo vai do acionamento ao táxi, "no ponto de espera da pista uno cinco" (o Solo manda chamar a Torre), alinhar e decolar, circuito, pouso e volta ao pátio. Quando o simulador mostra a decolagem ou o pouso, a Torre fala sozinha ("decolado aos ...", "no solo aos ..."); sem simulador, `/evento decolou` e `/evento pousou` fazem esse papel.

**Tempo:** o app busca o METAR do aeroporto em `aviationweather.gov` (só o código ICAO vai pela rede; o último fica guardado em `%LOCALAPPDATA%\Fonia\cache`). Com ele, a pista em uso é a que o vento favorece (`--runway` manda), e o controlador diz o vento e o ajuste de altímetro. Sem rede, ou com `--no-metar`, essas partes ficam de fora.

Por exemplo: `Solo Curitiba, PRABC, box 3, solicito pushback e acionamento`, depois o readback `autorizado pushback e acionamento, PRABC`, depois `PRABC, pronto para o táxi`. `sair` encerra.

Para testar o microfone e o push-to-talk (Windows; o texto ainda não é reconhecido, só gravado): segure a tecla (padrão: Ctrl direito), fale, solte, e a fala é salva em um WAV para você ouvir.

```bash
dotnet run --project src/Fonia.App -- --mic-list                                  # microfones e seus números
dotnet run --project src/Fonia.App -- --mic-test fala.wav                         # microfone padrão, tecla RControl
dotnet run --project src/Fonia.App -- --mic-test fala.wav --mic-device 1 --ptt-key F8
```

Para transcrever a gravação, localmente e sem internet, e ver o que o NLU entendeu, use o modelo [`ggml-small.bin`](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin) (466 MB, licença MIT), que o app baixa sozinho se faltar (ou use `--stt-dir` para outra pasta):

```bash
dotnet run --project src/Fonia.App -- --transcribe fala.wav --callsign PR-ABC          # CPU, ~2 s por frase
dotnet run --project src/Fonia.App -- --transcribe fala.wav --callsign PR-ABC --stt-gpu  # GPU (Vulkan), opcional
```

**Modelos de voz.** Os modelos (~800 MB, de código aberto) não vêm no repositório: o app os baixa dos donos, confere o SHA-256 e os guarda em `%LOCALAPPDATA%\Fonia\models`. Na primeira vez que um modo de voz precisar deles, ele lista o que falta e pergunta antes de baixar; ou baixe de uma vez:

```bash
dotnet run --project src/Fonia.App -- --download-models
```

Pode interromper (Ctrl+C) e rodar de novo: continua de onde parou. Uma distribuição que traga os modelos pode colocá-los em `models/` ao lado do executável, que é procurado primeiro. Créditos e licenças: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

**Conversa por voz** (Windows; precisa dos modelos de voz, que o app baixa se faltarem, e de microfone e alto-falante): segure o push-to-talk (Ctrl direito), fale como um piloto ("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento"), solte, e o controlador responde em voz alta. A tela mostra o que ele entendeu, a resposta em texto e o tempo de cada etapa. Enquanto ele fala, o PTT é ignorado, como num canal ocupado. Ctrl+C encerra.

```bash
dotnet run --project src/Fonia.App -- --atc SBCT --callsign PR-ABC --voice
dotnet run --project src/Fonia.App -- --atc SBCT --callsign PR-ABC --voice --stt-gpu --ptt-key F8 --audio-out 1   # GPU no reconhecimento, outra tecla e outro alto-falante
```

Para ouvir como o controlador soa (voz local, sem internet), sem o resto do pipeline: o modelo Kokoro-82M (Apache-2.0) é baixado pelo app se faltar. A fala sai em um WAV:

```bash
dotnet run --project src/Fonia.App -- --speak "PAPA ROMEO ALFA BRAVO CHARLIE, prossiga."                       # voz sorteada, efeito de rádio média
dotnet run --project src/Fonia.App -- --speak "Repita." --tts-voice pm_alex --radio-effect forte --speak-out fala.wav
```

O arquivo [CLAUDE.md](CLAUDE.md) descreve as convenções do projeto e orienta o trabalho com o Claude Code.

## Aviso

O Fonia é um projeto de entretenimento e simulação. **Não deve ser usado para treinamento aeronáutico real, navegação ou qualquer operação de voo real.** A fraseologia e os procedimentos implementados são aproximações para fins de simulação.

O projeto é independente e não tem afiliação com a Microsoft, a Asobo Studio, a Laminar Research, a SayIntentions.AI ou o DECEA. Os nomes de produtos citados pertencem aos seus respectivos donos.

## Créditos

Os aeroportos, pistas e frequências vêm do [OurAirports](https://ourairports.com/data/) (domínio público); veja [`data/ourairports/README.md`](data/ourairports/README.md).

## Licença

Distribuído sob a [Licença Apache 2.0](LICENSE). Copyright 2026 Leonardo Yuri Dalla Corte Brancalione. Veja também o arquivo [NOTICE](NOTICE).
