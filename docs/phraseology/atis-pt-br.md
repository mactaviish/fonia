# ATIS em português

Fonte: **ICA 100-37** (Serviços de Tráfego Aéreo), Subseção IV, Art. 759 a 777. O ATIS não está no MCA 100-16; a ICA 100-37 diz o que ele contém e em que ordem, e os exemplos dela são a referência da fala. O PDF fica fora do git (`/*.pdf`). Código: `AtisText` (Core).

## O que a norma diz

- **Designador (Art. 759):** uma letra do alfabeto; as letras seguem em ordem alfabética e a sequência recomeça à 0000 UTC. O piloto acusa o recebimento no primeiro contato com APP/Torre; o órgão informa o QNH ao responder.
- **Tipos (Art. 760):** de chegada, de saída ou combinado. O app monta o **combinado** (Art. 768).
- **Idioma (Art. 761 §2º):** gravado em português e em inglês. O app só tem o português; o inglês fica para o suporte a EN.
- **Mesmo conteúdo (Art. 763, parágrafo único):** dentro dos critérios SPECI o conteúdo é idêntico e o designador não muda.
- **Art. 766:** não é preciso repetir o que o piloto já confirmou, **exceto o QNH**, que sempre é informado. **Art. 767:** ATIS desatualizado acusado pelo piloto é corrigido na hora.

## Ordem do combinado (Art. 768) e o que o app faz

| # | Item | App |
|---|---|---|
| I | Aeródromo, informação, designador, hora da observação | Sim: "Curitiba, informação MIKE, hora UNO DOIS TRÊS ZERO" |
| II | Tipos de aproximação esperados | Omitido (sem dado). `TODO(fraseologia)` |
| III | Pista(s) em uso | Sim: "PISTA UNO CINCO em uso" (a pista que o vento favorece) |
| IV | Condições de pista / frenagem | Omitido (sem dado) |
| V | Atrasos por espera | Omitido |
| VI | Nível de transição | Omitido (sem dado) |
| VII | Outras informações essenciais | Omitido |
| VIII | Vento (graus magnéticos, nós) | Sim; a variação direcional (Art. 771) fica fora |
| IX | Visibilidade | Sim, regra do Art. 772 |
| X | Tempo presente | Sim (chuva, pancada, chuvisco, trovoada, granizo, nevoeiro, névoa úmida/seca, fumaça); CAVOK no lugar de IX a XI |
| XI | Nuvens (oitavos, base em pés; tipo só para cumulonimbos) | Sim, até três camadas |
| XII | Temperatura (°C inteiros) | Sim: "temperatura DOIS DOIS GRAUS" |
| XIII | QNH (hPa) | Sim: "ajuste UNO ZERO UNO OITO", sempre |
| XIV | Outras informações meteorológicas / tendência | Omitido |
| XV | Instrução para acusar o recebimento com o designador | Sim: "informe que recebeu informação MIKE" |

## Escolhas do app (não normativas)

- **Letra do designador:** a letra da **hora** da observação (00 = ALFA … 23 = X-RAY). Recomeça à 0000 UTC como manda o Art. 759 e é a mesma em qualquer execução do app para o mesmo METAR. Limitação: um SPECI na mesma hora mantém a letra, embora o conteúdo mude.
- **Cobertura de nuvens:** o METAR dá categorias; o ATIS fala oitavos (Art. 774). Usamos o meio da categoria: FEW 2, SCT 4, BKN 6, OVC 8. `TODO(fraseologia)`.
- **Visibilidade (Art. 772):** até 5.000 m em metros; de 5.000 m a 10 km em quilômetros inteiros (arredondado para baixo, `TODO(fraseologia)`); 10 km ou mais: "maior do que DEZ QUILÔMETROS". A informação de partida do Solo (Art. 122) ainda usa a proposta "DEZ QUILÔMETROS OU MAIS" para 9999; alinhar depois.
- **Intensidade do tempo presente** ("fraca", "forte") não está na norma: `TODO(fraseologia)`. Tempo presente "na vizinhança" (VC) fica de fora.
- **Altura de base** que não seja milhar redondo é dita dígito a dígito ("TRÊS ZERO ZERO PÉS"); os exemplos da norma só têm milhares redondos. `TODO(fraseologia)`.

## Exemplos da norma (referência)

- Combinado (Rio): "INTERNACIONAL DO RIO DE JANEIRO - INFORMAÇÃO ALFA - 12:30 ESPERE PROCEDIMENTO LOCALIZADOR X-RAY PISTA UNO CINCO - POUSO PISTA UNO CINCO - DECOLAGEM PISTA UNO ZERO NÍVEL DE TRANSIÇÃO ZERO MEIA ZERO - … VENTO UNO DOIS ZERO GRAUS OITO NÓS - RAJADA DOIS ZERO NÓS - VISIBILIDADE DOIS MIL METROS - … QUATRO OITAVOS CUMULONIMBOS UNO MIL PÉS - TEMPERATURA DOIS UNO GRAUS - AJUSTE UNO ZERO UNO UNO - … INFORME QUE RECEBEU INFORMAÇÃO ALFA."
- Chegada (Brasília) e saída (Congonhas) seguem o mesmo molde, sem os itens do outro sentido (Art. 769 e 770).
