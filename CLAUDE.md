# CLAUDE.md — Fonia

Este arquivo orienta o Claude Code ao trabalhar neste repositório.

## O que é o projeto

Aplicativo de ATC (controle de tráfego aéreo) por voz, com IA, para simuladores de voo. O piloto fala naturalmente no rádio, o app entende a intenção e responde com voz de controlador, usando fraseologia correta. Inspirado no SayIntentions.AI, com foco inicial em **português do Brasil** (fraseologia DECEA/ICAO) e suporte posterior a inglês (FAA/ICAO).

- **Nome do projeto:** Fonia (Fonia.IA). Namespace raiz: `Fonia`.
- **Linguagem:** C# / .NET (decisão tomada; não propor trocar de linguagem).
- **Licenciamento:** open-source sob **Apache-2.0** (arquivos `LICENSE` e `NOTICE` na raiz; titular: Leonardo Yuri Dalla Corte Brancalione). Não é necessário cabeçalho de licença nos arquivos `.cs`. O repositório ainda é privado até o mantenedor decidir publicá-lo.
- **MVP:** Microsoft Flight Simulator 2020 (SimConnect), Windows.
- **Futuro:** MSFS 2024 e X-Plane 11/12.

## Princípios inegociáveis

1. **O LLM nunca decide a resposta de ATC.** Ele (ou regras/regex) apenas *interpreta* a fala do piloto e devolve uma intenção estruturada (JSON desserializado e validado em um `record` tipado). Quem decide a resposta é código determinístico (máquina de estados). A fala do controlador sai de *templates* de fraseologia.
2. **O núcleo nunca fala direto com o simulador.** Tudo passa pela interface `ISimAdapter` (`Fonia.Core`). Nenhuma referência a SimConnect, socket do X-Plane etc. fora dos projetos `Fonia.Sim.*`.
3. **Tudo precisa rodar sem simulador.** Existe o `MockSimAdapter` e replay de telemetria gravada. Todo teste automatizado usa o mock.
4. **Funcionar offline** deve ser possível (STT e TTS locais). Serviços online (METAR, LLM em nuvem) são opcionais e com fallback.
5. **Módulos pequenos, entradas e saídas explícitas** (`record`s imutáveis). Sem estado global escondido; dependências por injeção.
6. **Licenças de dados:** usar apenas dados com licença aberta (ex.: OurAirports). Não incluir dados AIRAC/Navigraph ou outros proprietários no repositório.
7. **Licenças de dependências:** o projeto é Apache-2.0, então usar pacotes e modelos com licenças permissivas e compatíveis (MIT, Apache-2.0, BSD). Evitar GPL/AGPL, licenças "source-available" e as que restrinjam uso comercial (incluindo modelos de voz/IA "somente não comercial") sem avisar o mantenedor antes. Isso vale para bibliotecas, motores de voz e modelos de voz/IA. Ao propor uma dependência nova, informe a licença dela.
8. **O NLU entende o piloto, não o manual.** A fraseologia padronizada vale para o que o *controlador* fala. O piloto pode dizer a frase de qualquer jeito (com "bom dia", "ok", fora de ordem, abreviando o indicativo): o NLU procura a intenção e os dados em vez de exigir frases exatas, e as frases do manual servem como casos de teste, nunca como a única forma aceita.

## Pipeline

```
Áudio (PTT) → STT → NLU (fala → Intent) → Motor ATC (estado + regras) → Fraseologia (templates) → TTS → Áudio
                                              ↑
                                         ISimAdapter (telemetria)
```

Detalhes em `docs/ARCHITECTURE.md`.

## Stack

- **.NET 10 (LTS)**, C# com `Nullable` habilitado e warnings como erros nos projetos de produção
- Testes: **xUnit v3** (roda sobre Microsoft.Testing.Platform, ativado no `global.json`; exigido pelo SDK 10) e **Shouldly** (BSD-3) para asserções. Não usar FluentAssertions (licença comercial desde a v8)
- Qualidade: analyzers do .NET + `dotnet format`
- Infra: `Microsoft.Extensions.*` (DI, Configuration, Logging), `System.Text.Json`
- Simulador (MSFS): **SimConnect.NET** (MIT), atrás de uma interface interna para poder trocar; a escolha final é confirmada no spike do M1. Detalhes e riscos de licença da DLL em `docs/decisions/simconnect.md`
- Áudio: NAudio (Windows)
- STT / TTS locais: **avaliar no M3** (candidatos: Whisper.net e sherpa-onnx para STT; Piper via sherpa-onnx ou processo externo para TTS)

## Comandos

```bash
dotnet restore
dotnet build
dotnet test                       # roda todos os testes
dotnet format                     # formata
dotnet format --verify-no-changes # checa formatação (CI)
```

## Estrutura da solução

```
Fonia.sln
src/
  Fonia.Core/          # modelos, interfaces, NLU por regras, motor ATC, fraseologia (sem dependência de SO/simulador)
  Fonia.Data/          # aeroportos (OurAirports), METAR
  Fonia.Sim.Mock/      # MockSimAdapter, replay de telemetria
  Fonia.Sim.Msfs/      # MsfsSimAdapter (SimConnect) — somente Windows
  Fonia.Audio/         # captura, push-to-talk, playback — somente Windows
  Fonia.Speech/        # implementações de STT e TTS
  Fonia.App/           # executável (console primeiro, UI depois) e composição de dependências
tests/
  Fonia.Core.Tests/
  ...
docs/
```

**Regra de ouro dos projetos:** `Fonia.Core` não referencia nenhum outro projeto da solução. Os demais dependem do Core, nunca o contrário.

## Como trabalhar neste repositório

- **Uma tarefa pequena por vez.** Cada tarefa termina com build limpo, testes passando e um commit.
- **Plano antes de código** em mudanças que tocam mais de um projeto: proponha o plano e aguarde aprovação.
- **Testes primeiro** para o motor de ATC, NLU por regras e fraseologia (lógica pura).
- **Não invente fraseologia.** A fonte principal é o **MCA 100-16, edição 2025** do DECEA; ICAO Doc 4444 e a prática da VATSIM Brasil são secundárias. Fontes, catálogo e regras estão em `docs/phraseology/`. Se não houver referência para um caso, marque com `// TODO(fraseologia)` e pergunte ao mantenedor. A página oficial (<https://publicacoes.decea.mil.br/publicacao/mca-100-16>) avisa de nova edição em **26/11/2026**: conferir a edição vigente antes de mexer em templates. Não confie em PDFs antigos que circulam por aí (as edições de 2016, por exemplo, não têm `pushback`).
- **Código que usa API exclusiva do Windows em tempo de execução** (`Fonia.Audio`, `Fonia.Sim.Msfs`) também usa `net10.0`, não `net10.0-windows`: um projeto `net10.0` não pode referenciar um `net10.0-windows`, e o `Fonia.App` precisa compilar e ser testado em Linux (ambiente do Claude Code e CI). Só a execução (microfone, `GetAsyncKeyState`, DLL do SimConnect) é Windows; essas classes lançam `PlatformNotSupportedException` fora dele. Use `net10.0-windows` só se uma API em tempo de compilação exigir. Mantenha `<EnableWindowsTargeting>true</EnableWindowsTargeting>` para a solução compilar em Linux. Projetos de teste referenciam só Core e Mock; as exceções são `Fonia.Sim.Msfs.Tests` (referencia `Fonia.Sim.Msfs` e usa apenas uma sessão SimConnect falsa), `Fonia.App.Tests` (referencia `Fonia.App`), `Fonia.Data.Tests` (referencia `Fonia.Data`) e `Fonia.Audio.Tests` (referencia `Fonia.Audio`, sem dispositivo).
- **Código que depende do MSFS** só pode ser validado no Windows com o simulador aberto. Você não consegue rodá-lo. Escreva-o de forma defensiva (logs claros, exceções tratadas, reconexão) e peça ao mantenedor que teste e traga os logs.
- **Testes ao vivo do MSFS** (`LiveMsfsSessionTests`, categoria `Live`) rodam a sessão SimConnect real contra um MSFS 2020 aberto com um voo carregado, só lendo dados. Ficam ignorados por padrão (CI incluso); para rodá-los: `$env:FONIA_LIVE_MSFS = "1"; dotnet test --filter-class "*LiveMsfsSessionTests"`.
- **DLLs do SDK do MSFS** (SimConnect): verificar a licença antes de versionar no repositório. Se não puder, documentar o caminho de instalação do SDK e referenciá-las por variável de ambiente ou caminho configurável.
- **Async de ponta a ponta** no pipeline (`Task`, `CancellationToken`, `Channel<T>`). Não bloquear com `.Result` ou `.Wait()`.
- **Uma branch e um PR por tarefa.** O CI roda Linux (build, test, format) em todo PR (`ci.yml`) e Windows (build, test) em `ci-windows.yml`: em PRs que mexem em `src/`, `tests/` ou nas configurações de build, em todo push na `main` e manualmente (Actions, Run workflow) para testar uma branch antes do PR. Documentação e spikes não gastam minutos de Windows.
- **Commits** pequenos, mensagens no imperativo, em português ou inglês (manter consistência dentro do histórico).
- **Não adicione pacotes NuGet pesados** sem justificar (tamanho, licença, suporte a Windows).
- **Idioma:** código, nomes e comentários técnicos em inglês; documentação e textos de fraseologia em português quando aplicável.

## Definition of Done (para qualquer tarefa)

- [ ] `dotnet build` sem warnings novos
- [ ] Testes novos/atualizados e passando (`dotnet test`)
- [ ] `dotnet format --verify-no-changes` limpo
- [ ] Docs atualizadas se a arquitetura ou o comportamento mudou
- [ ] Nenhum acoplamento novo ao simulador fora de `Fonia.Sim.*`
