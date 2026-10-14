# Decisão: voz local (STT e TTS) para o M3

**Status: decidido.** STT: Whisper.net `small`. TTS: Kokoro-82M (ONNX) com camada de pronúncia própria. Ambos medidos e ouvidos pelo mantenedor em 2026-10-13. As vozes do Windows, o Piper e o Supertonic 3 foram reprovados na audição.

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
| **Vozes do Windows** (WinRT `SpeechSynthesizer`; Maria e Daniel pt-BR já vêm instaladas) | parte do Windows; usadas pela API, sem redistribuir nada | 0 | síntese em **~20 ms** por frase, ~6 s de áudio (medido) | **Descartada na audição** ("horríveis"). |
| **Piper** com vozes pt-BR | repositório original MIT, **arquivado em out/2025**; o ativo ([piper1-gpl](https://github.com/OHF-Voice/piper1-gpl)) é GPL-3.0. Vozes pt-BR: `faber`, `cadu`, `jeff` **CC0**; `edresson` CC BY 4.0 ([cartões dos modelos](https://huggingface.co/rhasspy/piper-voices/tree/main/pt/pt_BR)) | ~63 MB por voz | processo inteiro ~0,65 s (faber) e ~1,1 s (cadu) por frase, carregando o modelo a cada vez (medido) | **Descartado:** a voz não agradou; além disso, bloqueado por licença para embutir:** o Piper fonetiza com o **espeak-ng, GPL-3.0 ou posterior** ([espeak-ng](https://github.com/espeak-ng/espeak-ng)). As vozes são livres, o fonetizador não. Possível só como componente opcional que o usuário instala, decisão do mantenedor. |
| sherpa-onnx (TTS com Piper/VITS) | Apache-2.0, **mas as builds com TTS trazem o espeak-ng (GPL-3) compilado dentro** ([OpenWhispr #2340](https://github.com/OpenWhispr/openwhispr/pull/2340)). O projeto planeja remover o espeak-ng na versão 2.0, sem data ([issue #3731](https://github.com/k2-fsa/sherpa-onnx/issues/3731)) | idem | idem | Mesmo problema do Piper até a versão 2.0. Reavaliar quando sair (vozes com `lexicon.txt`). |
| **Kokoro-82M** | Apache-2.0 (pesos e código) | 325 MB fp32 + 28 MB de vozes | 0,8 a 1,3 s por frase em CPU; vozes pt-BR `pf_dora`, `pm_alex` | **Escolhido**, com fonetizador próprio no lugar do espeak-ng (seção abaixo). |
| Supertonic 3 | código MIT, modelo OpenRAIL-M; repositório arquivado | ~380 MB | 1,4 a 1,8 s em CPU, só `pt` | **Descartado:** sotaque estrangeiro na audição. |
| MMS-TTS, XTTS, Breeze TTS 2 | não comerciais | | | Descartados por licença. Chatterbox (MIT) é pesado (GPU de 4 a 6 GB). |

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

## O que o STT exige do NLU (tratado na tarefa "NLU tolerante ao STT", M3)

O texto que o STT entrega tem erros que o NLU precisa perdoar, dentro do que é seguro:

1. **Palavras emendadas** ("solicituacionamento", "pushbackeacionamento"): raízes de cinco letras ou mais (`acion`, `pushback`, `solicit`, `autorizad`...) são procuradas **dentro** da palavra. Raízes curtas (`tax`, `push`) continuam só no começo, para não casar com palavras demais.
2. **Indicativo com palavras fonéticas mal escritas** ("Papá Romeo, Alfa, Bravo Charly", "Chalie", "Charllie", "romeoalfa"): palavras fonéticas de 6 letras ou mais aceitam uma letra errada, a mais ou a menos; as curtas só exatas (senão "tanto" viraria o T de "tango" e "motel" o H de "hotel"); palavras emendadas são separadas.
3. **Pista escrita como número decimal** ("1,5", "3,3 esquerda"; "1-5" já funcionava): depois de "pista", ou num readback, um dígito, vírgula e mais dígitos é a pista ("15", "33L"), nunca uma frequência.

**Decisão: o indicativo com uma letra trocada NÃO é aceito** ("PRABD" não é PRABC; já era regra e tem teste). Aceitar "Prabac" ou "Prabic" como PRABC aceitaria também uma aeronave diferente, e o motor pede "repita o indicativo" até o piloto dizer o certo. A solução é o prompt com o indicativo falado, que fez o STT acertar 20 de 20.

Medido nas 20 gravações reais, sem prompt: 10/20 antes e depois (os erros que sobram, como "Bravucciarli", "Fábio Tcharly", "assinamento", são do STT e nenhuma regra segura recupera); com prompt: 20/20.

## Medições com a voz real (20 frases gravadas pelo mantenedor, 2026-10-13)

O indicativo foi falado por extenso ("Papa Romeo Alfa Bravo Charlie"), como manda a regra do projeto. O WER não vale aqui (as frases de referência traziam "PRABC" e "uno cinco", e o mantenedor falou de outro jeito); vale a intenção entendida pelo `RuleBasedNlu`.

| Modelo | Prompt | Intenção | Tempo/frase | Memória |
|---|---|---|---|---|
| small, Vulkan | "...PRABC." | 30% | 0,25 s | 621 MB |
| **small, CPU** | **com "Papa Romeo Alfa Bravo Charlie. Pista uno cinco."** | **100%** | 1,97 s | 849 MB |
| **small, Vulkan** | idem | **100%** | **0,23 s** | 621 MB |
| base, CPU | idem | 85% | 0,61 s | 413 MB |

- Com o prompt antigo, o Whisper escrevia "Paparromeu", "Bravucciarli", "Fubra Rutiardi" para o indicativo falado por extenso. **O prompt tem de trazer o indicativo como é falado** (alfabeto fonético), e o ideal é montá-lo com o indicativo do simulador.
- O Whisper escreve números como dígitos ("pista 15", "1-5", "1,5"). O NLU já lê "15"; falta "1-5" e "1,5" (tarefa do NLU).

## Regras de saída (vale para o TTS e para qualquer voz)

- **Pista sempre dígito a dígito:** "UNO CINCO", nunca "quinze". `SpokenText` já faz isso (também para frequência e QNH).
- **Indicativo sempre letra por letra**, no alfabeto fonético ("PAPA ROMEO ALFA BRAVO CHARLIE"), nunca como palavra.
- Qualquer TTS recebe o texto já escrito por extenso pela fraseologia; nenhum motor pode reinterpretar números ou siglas.

## Decisão

- **STT: Whisper.net (MIT) com o modelo `small` e prompt de vocabulário** montado com o indicativo falado. **CPU é o padrão** (~2 s por frase). **Vulkan opcional**, ligado em configuração: o mantenedor aceita uso leve de GPU, e o `small` cai para ~0,25 s em rajadas curtas. Se a GPU falhar, volta para a CPU. O `base` (0,6 s, 85%) fica como alternativa para máquinas fracas.
- **TTS: Kokoro-82M** (pesos e código Apache-2.0, segundo o [cartão do modelo](https://huggingface.co/hexgrad/Kokoro-82M)) rodando em ONNX Runtime (MIT), em CPU: 0,8 a 1,3 s por frase do controlador e 0,2 s para "Repita." (modelo fp32 de 325 MB, vozes 28 MB; existe versão int8 menor, a medir). Vozes pt-BR: `pf_dora` (feminina) e `pm_alex` (masculina). **`pm_santa` está excluída** por qualidade ruim (audição do mantenedor).

### Como o Kokoro entra sem espeak-ng

O Kokoro recebe **fonemas**, e os exemplos prontos obtêm os fonemas com o espeak-ng (GPL-3). Para não depender dele, o projeto tem um **fonetizador próprio de pt-BR** (`Fonia.Core.Speech`), em duas camadas:

1. **Camada de pronúncia** (dicionário por idioma, só para o TTS, o texto da fraseologia não muda): grafia aportuguesada de palavras estrangeiras e do alfabeto fonético. Testada na audição com `romeo→romeu`, `charlie→tchárli`, `pushback→púchibéqui`, `papa→pápa`, `alfa→álfa`, `bravo→brávo`, `uno→úno`; o mantenedor confirmou que "melhorou demais" e que as 6 frases de teste saíram certas. Sem ela, o Kokoro lia "charLIE" e "pushBAQUI".
2. **Texto → fonemas** (`PortuguesePhonemizer`) por regras do português do Brasil escritas para o projeto: acentos e sílaba tônica, "ti"/"di" suaves, `lh`, `nh`, `ch`, o "l" que soa como "u", vogais nasais, os cinco sons do "r", o "s" entre vogais, `qu`/`gu`, os cinco sons do "x", ditongos, o "i" de "rádio" como semivogal, tônicas abertas de uma lista curta de exceções e sílabas secundárias. A saída segue as convenções do alfabeto em que o Kokoro foi treinado (o "a" final como `æ`, o "o" final como `ʊ`, o "e" final como `y`), não a transcrição de livro, porque o modelo fala melhor o que ouviu no treino. Só usa símbolos do vocabulário do modelo (há teste), e os nasais saem com o til combinante, que é o que o modelo tem.

**Como foi validado:** o espeak-ng rodou apenas num ambiente temporário, como régua de comparação (a saída dele não foi copiada para o repositório, e as regras foram escritas independentemente). Nas 165 palavras comparadas (vocabulário dos templates e nomes dos aeroportos controlados), 144 saíram idênticas e as outras diferem por escolha (por exemplo, o artigo "o" sai `ʊ`, não `ˈɔ`, e o "l" final sai `ʊ`). Os testes de regressão (`PortuguesePhonemizerTests`) fixam a saída de 83 palavras já conferidas; o que cada regra faz está em `PortugueseRulesTests`. A aprovação final é de ouvido (o mantenedor).

O espeak-ng foi usado **só no spike e como régua de comparação**, num ambiente temporário; nada dele vai para o repositório.

### Implementação e medições (2026-10-14)

`KokoroSynthesizer` (`Fonia.Speech`) roda o modelo no **ONNX Runtime (MIT)**, na CPU, com metade dos núcleos (no máximo 8). Entradas do modelo: `tokens` (os fonemas numerados, com um zero em cada ponta), `style` (uma linha de 256 números, escolhida pela quantidade de fonemas, do arquivo de vozes) e `speed`; saída: áudio a 24 kHz. O arquivo de vozes é um `.npz` do numpy (zip com uma tabela `510x1x256` por voz), lido sem biblioteca de numpy. O silêncio que o modelo deixa no começo e no fim é cortado (o piloto espera por ele); textos de mais de 510 fonemas são divididos nas pausas.

Medido no i7-12700 com os arquivos do Kokoro (modelo fp32), voz `pm_alex`, CPU:

| | Tempo |
|---|---|
| Carga do modelo (primeira chamada) | ~1,2 s |
| "Repita." (0,9 s de fala) | ~0,17 s |
| "...prossiga." (2,5 s de fala) | ~0,43 s |
| Frase de táxi (5,5 s de fala) | ~0,9 s |
| Memória de pico | ~520 MB |

Com 4 threads fica ~25% mais lento; com 8, igual ao padrão (6 neste processador). O efeito de rádio acrescenta menos de 10 ms.

### Latência medida ao vivo (2026-10-14)

Primeira sessão real com `--atc SBCT --voice`, **CPU** (sem `--stt-gpu`), i7-12700 com o MSFS fora do ar. Do PTT solto até o áudio pronto para tocar, por etapa (linha `tempos:` do app):

| Fala do controlador | Reconhecimento | Entendimento | Voz | Rádio | **Até o áudio** |
|---|---|---|---|---|---|
| "...chame Torre Curitiba UNO UNO OITO DECIMAL CINCO CINCO." (frase longa) | 2,2 s | 2 ms | 891 ms | 15 ms | **3,2 s** |
| "Repita." (3 trocas) | 2,1 a 2,2 s | 0 a 1 ms | 170 a 178 ms | 3 ms | **2,3 a 2,4 s** |

- O **reconhecimento é o gargalo**: ~2,1 a 2,2 s por fala de ~4 s, igual ao medido no spike (1,97 s). A síntese, que era a dúvida, ficou em 0,17 s a 0,9 s, e o efeito de rádio e as regras (NLU, motor, fraseologia) somam menos de 20 ms.
- Total **2,3 a 3,2 s**, dentro do alvo "abaixo de ~3 s" para respostas curtas e no limite para as longas. Com `--stt-gpu` o reconhecimento cai para ~0,25 s e o total para ~1 a 1,5 s (a medir ao vivo, com o simulador aberto, que é onde a GPU é disputada).
- Medido sem o MSFS aberto: com o simulador consumindo CPU o reconhecimento deve subir; é o número a repetir no teste de voo completo.

### Ajustes de voz por audição (2026-10-14)

Dois achados do teste ao vivo foram resolvidos gerando variantes com o mesmo modelo, fonetizador e efeito de rádio do app, e o mantenedor ouvindo (mesma frase nas duas vozes aprovadas):

| Problema | Variantes ouvidas | Escolhida |
|---|---|---|
| "Repita." sai "repida" (9 fonemas) | atual; linha de estilo de 30 e de 60 fonemas; `t` aspirado; respiro `…` no começo; **com o indicativo** | **"PAPA ROMEO ALFA BRAVO CHARLIE, repita."** |
| quase sem pausa entre "negativo" e "autorizado" | vírgula (atual); ponto; **reticências**; duas sínteses com 250 ms; três com 200 ms; duas com 400 ms | **"negativo..."** |

Pausa medida depois de "negativo" (silêncio abaixo de 4% do pico, voz `pf_dora` / `pm_alex`): vírgula ~230 / ~210 ms; **reticências ~340 / ~340 ms**; ponto ~410 / ~370 ms; 250 ms de silêncio entre duas frases ~370 ms; 400 ms ~520 ms. O chiado do efeito de rádio preenche parte do silêncio e reduz a impressão de pausa, então vale medir as pausas pela voz limpa, como acima.

Com as reticências a correção ganha uns 110 ms de pausa sem custo de síntese (uma única chamada ao modelo, ao contrário de cortar a frase em duas).

### Obtenção dos modelos (2026-10-14)

Primeiro teste ao vivo do `--voice` bateu em "modelo não encontrado", e a regra "o usuário baixa na mão" caiu: o app baixa os modelos dos donos, **com confirmação**, e confere o SHA-256. Os arquivos não vão para o repositório (o GitHub recusa mais de 100 MB e o Git LFS gratuito não comporta) nem são redistribuídos pelo projeto, o que também dispensa a pergunta sobre os dados de treino do Kokoro (o dono do modelo é quem distribui).

| Arquivo | Tamanho | SHA-256 | Origem do hash |
|---|---|---|---|
| `ggml-small.bin` | 487.601.967 | `1be3a9b2...fea987b` | o `X-Linked-ETag` que o Hugging Face publica |
| `kokoro-v1.0.onnx` | 325.532.387 | `7d5df8ec...3636a6c5` | calculado em dois downloads do release `model-files-v1.0` |
| `voices-v1.0.bin` | 28.214.398 | `bca610b8...1fbf7d` | idem |

Lido na época: o cartão do Kokoro diz Apache-2.0 para os pesos, "treinado exclusivamente" com áudio de domínio público ou sob licenças como Apache e MIT, mais áudio sintético de provedores de TTS fechados, com atribuição CC BY para Koniwa (CC BY 3.0) e SIWIS (CC BY 4.0). Não diz nada sobre restrição de redistribuição ou de uso comercial. Fica em `THIRD-PARTY-NOTICES.txt`. O README do `kokoro-onnx` cita `model-files-v1.1` e o app baixa de `v1.0`, que é o que foi medido e conferido.

Testado contra os servidores de verdade: ~50 a 75 MB/s, interrupção no meio do Kokoro e retomada, hash conferido ao final.

### Configuração de voz (pedido do mantenedor)

- O usuário escolhe a voz nas configurações.
- **Padrão: aleatória** entre as vozes aprovadas (hoje `pf_dora` e `pm_alex`), para diversificar; cada controlador/serviço pode manter a mesma voz durante a sessão. Mais vozes entram na lista conforme forem avaliadas.

### Efeito de rádio (pedido do mantenedor)

Módulo `RadioEffect`, aplicado depois do TTS: filtro passa-faixa, saturação suave (tanh), chiado limitado à mesma banda, clique de abertura do PTT e "squelch" de fechamento. Só NAudio (`BiQuadFilter`) e amostras geradas no código, sem pacote novo; custo desprezível. Prototipado em Python e **a intensidade ainda precisa ser aprovada na audição**.

| Intensidade | Banda | Saturação (drive) | Chiado | Clique |
|---|---|---|---|---|
| desligado | sem filtro | - | - | - |
| leve | 250-3800 Hz | 1,5 | 0,004 | 0,10 |
| **média (padrão)** | 350-3200 Hz | 2,5 | 0,010 | 0,18 |
| forte | 450-2800 Hz | 4,0 | 0,022 | 0,28 |

O usuário escolhe a intensidade nas configurações; os números acima são o ponto de partida e podem ser ajustados depois da audição.

### Descartados na audição

Vozes do Windows (Maria, Daniel): "horríveis". Piper (faber, cadu): não agradou, e depende do espeak-ng. Supertonic 3: sotaque estrangeiro ("de indiano"), além da licença OpenRAIL-M e do repositório arquivado. Kokoro `pm_santa`: ruim.

## Como reproduzir

```
dotnet run tools/speech-spike.cs -- tts                       # vozes do Windows (descartadas)
dotnet run tools/speech-spike.cs -- record                    # grava as frases com o microfone (não vai para o git)
dotnet run tools/speech-spike.cs -- synth                     # gera as frases com voz sintética
dotnet run tools/speech-spike.cs -- stt --model small --prompt [--runtime vulkan] [--quant q5_1]
```

Modelos e áudios ficam em `%TEMP%\fonia-speech-spike`, fora do repositório. O programa de medição não faz parte da solução.

### Conjunto de voo (cotejamentos reais, 2026-10-14)

Depois do primeiro teste de voo no MSFS (muitos erros do STT em cotejamentos), o spike ganhou um segundo conjunto de 36 frases de voo (`tools/speech-spike/phrases-voo.txt`: SBCT, PR-FRA, Solo e Torre, do acionamento ao táxi para o pátio). Aqui o resultado não é só a intenção: é **o que o controlador faria** (o tipo da instrução do motor ATC; `NoResponse` é um cotejamento aceito, `RequestReadbackCorrection` é "negativo"), mais a taxa de indicativo reconhecido.

```
dotnet run tools/speech-spike.cs -- record --phrases phrases-voo.txt               # grava em tools/speech-spike/recordings-voo (fora do git)
dotnet run tools/speech-spike.cs -- stt --phrases phrases-voo.txt --model small [--prompt] [--ctx] [--runtime vulkan] [--quant q5_0]
```

`--prompt` usa o prompt do app (vocabulário, estação e indicativo); `--ctx` acrescenta a última fala do controlador. Modelos: `tiny`, `base`, `small`, `medium`, `large-v3-turbo`.

Teste do roteiro com a voz sintética do Windows (só prova o processo, não vale como medida do app), `small`, CPU:

| Prompt | O controlador faria o certo | Indicativo reconhecido | Tempo/frase |
|---|---|---|---|
| nenhum | 7/36 (19%) | 0/36 | 2,3 s |
| do app | 31/36 (86%) | 34/36 | 2,5 s |
| do app + última fala | 31/36 (86%) | 35/36 | 2,5 s |

O prompt é o que mais pesa; o da última fala não mudou o acerto na voz sintética, e três cotejamentos saíram só com o indicativo (o Whisper tende a omitir o que repete o prompt). Quem decide é a medição com a voz do mantenedor.
