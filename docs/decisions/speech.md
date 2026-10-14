# Decisão: voz local (STT e TTS) para o M3

**Status: STT decidido; TTS em aberto.** O STT foi medido com a voz real do mantenedor e está decidido. As vozes do Windows foram **reprovadas na audição** ("horríveis"), então o TTS depende de outra pesquisa e de uma decisão de licença do mantenedor (seção "Decisão").

Medido em 2026-10-13 em um i7-12700 (12 núcleos), 32 GB, RTX 3070 Ti, Windows 11, .NET 10.

## O que a escolha precisa cumprir

- **Leve:** roda ao lado do MSFS. A referência é **CPU**; a GPU é do simulador e só entra como opção.
- **Licença permissiva, do código e do modelo** (CLAUDE.md, princípio 7): MIT, Apache-2.0, BSD ou equivalente. GPL/AGPL, "somente não comercial" e "source-available" precisam do aviso do mantenedor.
- **Offline**, fácil de empacotar, com bom pt-BR.
- **Rápido:** da hora que o piloto solta o PTT até o controlador começar a falar, o ideal é bem abaixo de 3 s.

## STT (fala → texto)

| Opção | Licença (código / modelo) | Tamanho | pt-BR | Veredito |
|---|---|---|---|---|
| **Whisper.net** + modelos ggml do whisper.cpp | MIT / MIT ([Whisper.net](https://github.com/sandrohanea/whisper.net), [modelos](https://huggingface.co/ggerganov/whisper.cpp)) | tiny 75 MB, base 142 MB, small 466 MB (quantizados: small q5_1 181 MB, q8_0 252 MB) | sim (multilíngue) | **Candidato principal.** Aceita *prompt* de vocabulário (`WithPrompt`, marcado experimental), roda em CPU, Vulkan e CUDA, pacotes NuGet por runtime. |
| sherpa-onnx (ASR) | Apache-2.0 | depende do modelo | Whisper em ONNX; poucos modelos pt-BR nativos | **Cuidado:** os pacotes padrão incluem o espeak-ng (GPL-3) compilado junto, mesmo para quem só quer STT (veja abaixo). Dá para usar as builds `-no-tts`, mas empacotar isso é trabalho extra. Sem vantagem clara sobre o Whisper.net. |
| Vosk `small-pt-0.3` | Apache-2.0 / Apache-2.0 | 31 MB | fraco: WER 32,6% (Common Voice) e 68,9% (CORAA, fala espontânea) segundo a [lista oficial](https://alphacephei.com/vosk/models) | Descartado: o modelo grande de pt-BR é **GPLv3** e o pequeno erra demais. |
| Reconhecedor do Windows (`System.Speech`) | parte do Windows | 0 | **não instalado nesta máquina**: só `en-US` | Descartado: depende de pacote de idioma que o usuário pode não ter e é fraco para fala livre. |
| Parakeet (runtime do Whisper.net) | a verificar | ~356 MB (Q4_0) | multilíngue, não confirmado | **Não avaliado.** Não aceita *prompt*, que é o que mais ajuda aqui. Fica como possibilidade futura. |

## TTS (texto → voz)

| Opção | Licença | Tamanho | Qualidade / tempo | Veredito |
|---|---|---|---|---|
| **Vozes do Windows** (WinRT `SpeechSynthesizer`; Maria e Daniel pt-BR já vêm instaladas) | parte do Windows; usadas pela API, sem redistribuir nada | 0 | síntese em **~20 ms** por frase, ~6 s de áudio (medido) | **Candidato principal e padrão.** Zero risco de licença e de empacotamento. Só Windows, o que já é o caso do MSFS. Qualidade: precisa de audição. |
| **Piper** com vozes pt-BR | repositório original MIT, **arquivado em out/2025**; o ativo ([piper1-gpl](https://github.com/OHF-Voice/piper1-gpl)) é GPL-3.0. Vozes pt-BR: `faber`, `cadu`, `jeff` **CC0**; `edresson` CC BY 4.0 ([cartões dos modelos](https://huggingface.co/rhasspy/piper-voices/tree/main/pt/pt_BR)) | ~63 MB por voz | processo inteiro ~0,65 s (faber) e ~1,1 s (cadu) por frase, carregando o modelo a cada vez (medido) | **Bloqueado por licença para embutir:** o Piper fonetiza com o **espeak-ng, GPL-3.0 ou posterior** ([espeak-ng](https://github.com/espeak-ng/espeak-ng)). As vozes são livres, o fonetizador não. Possível só como componente opcional que o usuário instala, decisão do mantenedor. |
| sherpa-onnx (TTS com Piper/VITS) | Apache-2.0, **mas as builds com TTS trazem o espeak-ng (GPL-3) compilado dentro** ([OpenWhispr #2340](https://github.com/OpenWhispr/openwhispr/pull/2340)). O projeto planeja remover o espeak-ng na versão 2.0, sem data ([issue #3731](https://github.com/k2-fsa/sherpa-onnx/issues/3731)) | idem | idem | Mesmo problema do Piper até a versão 2.0. Reavaliar quando sair (vozes com `lexicon.txt`). |
| Kokoro, MMS-TTS, XTTS e outros | não verificadas | | | **Fora desta rodada.** Antes de qualquer uso, confirmar a licença do modelo e se dependem do espeak-ng. |

## Medições de STT (voz sintética: Microsoft Maria falando as 20 frases de `tools/speech-spike/phrases.txt`)

"Intenção" é quanto o `RuleBasedNlu` entendeu certo a partir do texto transcrito (tipo de pedido e se achou o indicativo). WER é a taxa de erro de palavras. Tempo é por frase de ~4,5 s de áudio, CPU com 10 threads, modelo já carregado.

| Modelo | Prompt | Intenção | WER | Tempo/frase | Memória de pico |
|---|---|---|---|---|---|
| tiny | não | 10% | 84% | 0,29 s | 286 MB |
| base | não | 10% | 81% | 0,59 s | 402 MB |
| base | **sim** | 55% | 47% | 0,63 s | 409 MB |
| small | não | 15% | 51% | 1,88 s | 838 MB |
| small | **sim** | **65%** | **28%** | 1,92 s | 845 MB |
| small q5_1 | sim | 60% | 34% | 1,87 s | 564 MB |
| small, **Vulkan** | sim | 65% | 28% | **0,24 s** | 621 MB (carga inicial: 10 s) |

O que isso mostra:

- **O *prompt* de vocabulário (aviação, "pushback", "acionamento", o indicativo) é decisivo**: sobe a intenção de 10-15% para 55-65%. Não é opcional.
- **Quantizar (q5_1) não acelera na CPU**, só reduz o disco (181 MB) e a memória (564 MB), com uma pequena perda de acerto.
- **A GPU via Vulkan faz o `small` levar 0,24 s** em vez de 1,9 s. Roda na placa do simulador por rajadas curtas; vale como opção, não como exigência. O CUDA 12 não ajudou aqui (mesmo tempo e memória da CPU, então parece ter caído para ela); o Vulkan atende a qualquer placa.
- **`base` com prompt** (0,6 s na CPU) pode ser o ponto de equilíbrio sem GPU; só as gravações reais dizem se o acerto basta.

**Limite desta medição:** a voz sintética é uma má representante da sua voz. O que mais derrubou o acerto foi o **indicativo**: o Whisper escreve "Prabc", "Prabac", "Prabic", "Právix", "Prabe" para "PRABC". E a voz sintética emenda palavras ("solicituacionamento"), o que uma pessoa faz menos.

## O que isso muda no NLU (a tratar em tarefa própria)

Independentemente de qual modelo vencer, o texto que o STT entrega tem erros que o NLU de hoje não perdoa:

1. **Indicativo com 1 letra trocada** ("Prabac", "Prabic", "Prabe" para PRABC): comparar com tolerância (distância de edição ≤ 1, ou por fonemas) e usar o indicativo conhecido do simulador no *prompt*.
2. **Palavras emendadas** ("solicituacionamento"): procurar as raízes (`acion`, `pushback`) dentro da palavra, não só no começo.
3. **Números escritos de vários jeitos** ("1,5", "1-5", "quinze", "um cinco"): já tratados em parte pelo `SpokenCode`, falta "1,5" e "1-5".

## Medições com a voz real (20 frases gravadas pelo mantenedor, 2026-10-13)

O indicativo foi falado por extenso ("Papa Romeo Alfa Bravo Charlie"), como manda a regra do projeto. O WER não vale aqui (as frases de referência traziam "PRABC" e "um cinco", e o mantenedor falou de outro jeito); vale a intenção entendida pelo `RuleBasedNlu`.

| Modelo | Prompt | Intenção | Tempo/frase | Memória |
|---|---|---|---|---|
| small, Vulkan | "...PRABC." | 30% | 0,25 s | 621 MB |
| **small, CPU** | **com "Papa Romeo Alfa Bravo Charlie. Pista uno cinco."** | **100%** | 1,97 s | 849 MB |
| **small, Vulkan** | idem | **100%** | **0,23 s** | 621 MB |
| base, CPU | idem | 85% | 0,61 s | 413 MB |

- Com o prompt antigo, o Whisper escrevia "Paparromeu", "Bravucciarli", "Fubra Rutiardi" para o indicativo falado por extenso. **O prompt tem de trazer o indicativo como é falado** (alfabeto fonético), e o ideal é montá-lo com o indicativo do simulador.
- O Whisper escreve números como dígitos ("pista 15", "1-5", "1,5"). O NLU já lê "15"; falta "1-5" e "1,5" (tarefa do NLU).

## Regras de saída (vale para o TTS e para qualquer voz)

- **Pista sempre dígito a dígito:** "UNO CINCO", nunca "quinze" nem "um cinco". `SpokenText` já faz isso (também para frequência e QNH).
- **Indicativo sempre letra por letra**, no alfabeto fonético ("PAPA ROMEO ALFA BRAVO CHARLIE"), nunca como palavra.
- Qualquer TTS recebe o texto já escrito por extenso pela fraseologia; nenhum motor pode reinterpretar números ou siglas.

## Decisão

- **STT: Whisper.net (MIT) com o modelo `small` e prompt de vocabulário** montado com o indicativo falado. **CPU é o padrão** (~2 s por frase). **Vulkan opcional**, ligado em configuração: o mantenedor aceita uso leve de GPU, e o `small` cai para ~0,25 s em rajadas curtas. Se a GPU falhar, volta para a CPU. O `base` (0,6 s, 85%) fica como alternativa para máquinas fracas.
- **TTS: em aberto.** As vozes do Windows (Maria, Daniel) não servem. As vozes do Piper (`faber`, `cadu`, CC0) são o caminho de qualidade conhecido, mas dependem do espeak-ng (GPL-3). Opções para o mantenedor decidir:
  1. **Piper como componente opcional**, instalado pelo usuário e chamado como processo separado (nada GPL entra no repositório nem no instalador); voz do Windows como reserva.
  2. **Pesquisar mais** (Kokoro, VITS via sherpa-onnx sem espeak-ng, outras vozes pt-BR) antes de aceitar a opção 1. Como a fala do controlador sai de templates fixos, outra via é pré-gerar e embutir trechos de áudio, o que exige voz com licença que permita redistribuir.

## Como reproduzir

```
dotnet run tools/speech-spike.cs -- tts                       # vozes do Windows
dotnet run tools/speech-spike.cs -- record                    # grava as frases com o microfone (não vai para o git)
dotnet run tools/speech-spike.cs -- synth                     # gera as frases com voz sintética
dotnet run tools/speech-spike.cs -- stt --model small --prompt [--runtime vulkan] [--quant q5_1]
```

Modelos e áudios ficam em `%TEMP%\fonia-speech-spike`, fora do repositório. O programa de medição não faz parte da solução.
