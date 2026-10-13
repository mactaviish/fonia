# Fonia

**ATC por voz com IA para simuladores de voo, em português do Brasil.**

Fonia (Fonia.IA) é um aplicativo que roda ao lado do simulador e permite que você converse com controladores de tráfego aéreo usando a própria voz, no rádio, com fraseologia correta. Você fala naturalmente, o app entende o que você pretende fazer e responde como um controlador responderia.

> **Status: em desenvolvimento inicial.** Ainda não há ATC por voz. Hoje o app lê o estado da aeronave no Microsoft Flight Simulator 2020 em tempo real, reconecta sozinho se o simulador fechar e abrir, e grava e reproduz sessões (marcos M0 e M1). Este README descreve o objetivo e o rumo do projeto. Acompanhe o progresso no [roadmap](docs/ROADMAP.md).

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

1. Você aperta o push-to-talk e fala: *"Torre de Curitiba, PR-ABC, solicito táxi para a pista 15"*.
2. O app transcreve a fala e extrai a intenção (solicitação de táxi, pista 15, matrícula PR-ABC).
3. O motor de ATC consulta o estado da aeronave e do aeroporto e decide a instrução.
4. A fraseologia monta a resposta e a voz do controlador fala de volta.

Mais detalhes em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Simuladores

| Simulador | Status |
|---|---|
| Microsoft Flight Simulator 2020 | Alvo do MVP |
| Microsoft Flight Simulator 2024 | Planejado |
| X-Plane 11 / 12 | Planejado |

## Funcionalidades planejadas

- Posições de Solo e Torre (partida, pushback, táxi, decolagem, pouso)
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
dotnet run --project src/Fonia.App                 # lê o MSFS
dotnet run --project src/Fonia.App -- --sim mock   # taxi simulado, sem simulador
dotnet run --project src/Fonia.App -- --help       # todas as opções (gravar e reproduzir sessões)
```

O arquivo [CLAUDE.md](CLAUDE.md) descreve as convenções do projeto e orienta o trabalho com o Claude Code.

## Aviso

O Fonia é um projeto de entretenimento e simulação. **Não deve ser usado para treinamento aeronáutico real, navegação ou qualquer operação de voo real.** A fraseologia e os procedimentos implementados são aproximações para fins de simulação.

O projeto é independente e não tem afiliação com a Microsoft, a Asobo Studio, a Laminar Research, a SayIntentions.AI ou o DECEA. Os nomes de produtos citados pertencem aos seus respectivos donos.

## Licença

Distribuído sob a [Licença Apache 2.0](LICENSE). Copyright 2026 Leonardo Yuri Dalla Corte Brancalione. Veja também o arquivo [NOTICE](NOTICE).
