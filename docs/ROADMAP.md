# Roadmap

Cada marco termina com testes passando e algo que você consegue demonstrar. Marcos são pequenos de propósito: bom para trabalhar com o Claude Code, uma tarefa de cada vez.

## M0: Fundação

- [x] Repositório privado, `Fonia.sln`, `Directory.Build.props` (Nullable, analyzers, `EnableWindowsTargeting`)
- [x] Projetos iniciais: `Fonia.Core`, `Fonia.Sim.Mock`, `Fonia.Core.Tests`
- [x] `CLAUDE.md`, `docs/ARCHITECTURE.md`, este roadmap
- [x] Modelos (`record`): `AircraftState`, `Intent`, `AtcDecision`, `FlightContext`
- [x] Interface `ISimAdapter` + `MockSimAdapter` (estado programável)
- [x] CI (GitHub Actions): `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes` em Linux e Windows

**Concluído em 2026-10-12.** CI verde em Linux e Windows, mock com estados programáveis e sequências de teste.

**Pronto quando:** `dotnet test` passa no CI nos dois sistemas e o mock produz estados de teste.

## M1: Ler o MSFS 2020

- [ ] Avaliar e escolher a abordagem de SimConnect (SDK oficial ou wrapper), e resolver o licenciamento das DLLs
- [ ] `MsfsSimAdapter`: posição, altitudes, velocidade, em solo, COM1/COM2, transponder, matrícula
- [ ] Aplicativo de console que imprime o estado em tempo real
- [ ] Gravador de telemetria (salva sessão em arquivo) e replay via `MockSimAdapter`
- [ ] Reconexão automática se o simulador for aberto depois do app

**Pronto quando:** você abre o MSFS 2020, roda o app e vê os dados corretos mudando; uma sessão gravada pode ser reproduzida sem o simulador.

## M2: ATC por texto (Solo)

- [ ] Carregar um aeroporto (OurAirports): pistas e frequências
- [ ] NLU por regras para: `RequestStartup`, `RequestPushback`, `RequestTaxi`, `Readback`, `SayAgain`
- [ ] Máquina de estados do Solo
- [ ] Templates de fraseologia pt-BR para essas instruções
- [ ] Console: você digita a fala do piloto, o app responde em texto, usando o estado real (ou mockado) do simulador

**Pronto quando:** no terminal, você faz o ciclo partida → pushback → táxi com readbacks, e os testes cobrem os caminhos felizes e erros comuns (readback errado, pista inexistente).

## M3: Voz

- [ ] **Spike:** comparar opções de STT e TTS locais em .NET (precisão em pt-BR, latência, tamanho, licença, facilidade de empacotar) e registrar a decisão em `docs/`
- [ ] Captura de áudio + push-to-talk (`Fonia.Audio`)
- [ ] `ITranscriber` com a opção escolhida (modelo pequeno, prompt de aviação)
- [ ] `ISynthesizer` com voz pt-BR
- [ ] Integrar ao pipeline do M2
- [ ] Medir e registrar latência por etapa

**Pronto quando:** você aperta PTT, fala "solicito táxi" e ouve a resposta do Solo, sem digitar nada.

## M4: Voo completo em um aeroporto

- [ ] Posição Torre: alinhar, autorização de decolagem, autorização de pouso
- [ ] Transferência de frequência entre posições (Solo → Torre) conforme o COM1 do simulador
- [ ] ATIS com METAR real (cache + fallback)
- [ ] Seleção de pista em uso pelo vento
- [ ] Configuração em `appsettings.json`; primeira versão utilizável por outras pessoas
- [ ] Publicação (`dotnet publish` self-contained) e primeiro instalador Windows
- [ ] Documentação de instalação

**Pronto quando:** você faz um voo local completo (partida → táxi → decolagem → circuito → pouso → táxi) falando com o app.

## M5: Expansão

- [ ] Aproximação / Centro e handoffs
- [ ] Tráfego de IA no rádio (outras aeronaves falando)
- [ ] Suporte ao MSFS 2024 (validar `MsfsSimAdapter`)
- [ ] `XPlaneSimAdapter` (X-Plane 11/12)
- [ ] Inglês (FAA/ICAO)
- [ ] NLU com LLM opcional para fala fora do padrão
- [ ] Integração com SimBrief (plano de voo)
- [ ] Interface gráfica (WPF, WinUI 3 ou Avalonia)

## Backlog / ideias

- Sotaques e vozes diferentes por posição e aeroporto
- Efeito de rádio realista (ruído, queda de sinal por distância)
- Emergências (MAYDAY/PAN-PAN)
- Integração com addons de aeroportos
- Atualização automática do app

## Nome do projeto

Nome escolhido: **Fonia** (Fonia.IA); namespace `Fonia`. Antes de publicar:

- [x] Escolher o nome definitivo
- [ ] Verificar disponibilidade (GitHub, NuGet, domínio, marcas existentes na área de simulação de voo)
