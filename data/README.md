# Dados

## `aisweb/br-aisweb-frequencies.csv`: frequências e nomes oficiais (AISWEB/DECEA)

Os serviços de comunicação de cada aeródromo no ROTAER, com o nome oficial e as frequências. **Os dados são do DECEA**, obtidos pela API do AISWEB e usados nos termos da Política de Uso (§10); não estão sob a Apache-2.0 (veja o `NOTICE`) e **não servem para navegação real**. `aisweb/snapshot.txt` guarda a data da coleta e as datas de atualização dos aeródromos.

| Coluna | Significado |
|---|---|
| `icao` | Código do aeródromo. |
| `name`, `city`, `uf` | Nome oficial, município e estado. |
| `service` | O serviço, como o ROTAER o chama (`Torre`, `Solo`, `Tráfego`, `ATIS`, `Operações`, `Pátio`...). |
| `callsign` | O nome falado do órgão no ROTAER (`Guarulhos`, `São Paulo`). |
| `frequency_mhz` | A frequência em MHz. Um serviço pode ter várias, uma linha para cada. |
| `note` | O complemento do ROTAER para aquela frequência (`EMERG.` para 121,5; `SRY`, `ALTN`, `PRI`, `DCL.`, `D-ATIS`...). Vazio quando não há. |

**Como o app usa** (embutido no `Fonia.Data`, lido pelo `AirportCatalog`): para cada tipo de serviço que o arquivo traz (Torre, Solo, Tráfego, ATIS, AFIS, Rádio, Operações), as frequências dele **substituem** as do OurAirports naquele aeródromo; o que ele não traz (o APP, por exemplo) fica como estava. O simulador, quando há, vence os dois. Regras da nota:
- `EMERG`, `Emergência`, `ACFT MIL`: a frequência não entra. O 121,5 também não entra nunca, com ou sem nota.
- `SRY`, `ALTN`, `Alternativa`, `reserva`, `secundária`: entra e atende quando sintonizada, mas o app não a cita no "chame a Torre ...".
- `PRI`, `Principal`, `primária`: é a que o app cita. Sem nenhuma marcada, é a primeira sem nota de secundária.
- Horários e telefones na nota são ignorados por enquanto.
- `Pátio` e `FCA` não são órgãos que o piloto chama e não entram. O nome falado **não** vem daqui (o `callsign` do ROTAER é inconsistente): continua o `atc-names.csv`.

**Como atualizar** (a cada ciclo AIRAC): defina `FONIA_AISWEB_KEY` e `FONIA_AISWEB_PASS` (nunca no repositório) e rode `dotnet run tools/update-aisweb.cs`. São cerca de uma chamada por aeródromo, uma por segundo. A lista pedida é a dos aeroportos do OurAirports com voo regular, porte grande ou médio, alguma frequência ou nome falado. Para só alguns: `-- --icao SBGR,SBSP`.

## `runway-use.csv`: pista de pouso e pista de decolagem

Aeroportos com pistas paralelas de usos diferentes, onde a mesma direção tem uma pista para os pousos e outra para as decolagens (Guarulhos: com a 10 em uso, a 10R recebe os pousos e a 10L as decolagens; com a 28, a 28L e a 28R). O vento ou o usuário escolhe a direção; a tabela diz quem faz o quê. Um aeroporto que não está no arquivo usa uma pista só para os dois, como sempre.

| Coluna | Significado |
|---|---|
| `icao` | Código do aeroporto. |
| `arrival` | A cabeceira que recebe os pousos (`10R`). |
| `departure` | A cabeceira das decolagens (`10L`). |
| `source` | De onde veio (`mantenedor`: informado por quem ouve a Torre). |
| `notes` | Observações e a data. |

Uma linha por direção. **São informações operacionais, não de manual:** confirme com a Torre real ou a carta do aeroporto antes de acrescentar uma linha. Uma linha com uma pista que o aeroporto não tem é ignorada na hora de usar.

## `atc-names.csv`: nome falado dos aeroportos controlados

O nome que o controlador fala depois do órgão ("Solo **Galeão**", "Torre **Guarulhos**", MCA 100-16, Art. 37). O município dos dados do OurAirports não serve: erra justamente nos aeroportos mais usados (SBGR sai "São Paulo", SBKP "Campinas", SBCF "Belo Horizonte", SBGL "Rio De Janeiro").

O arquivo cobre os aeroportos que têm **Solo ou Torre** nos dados. É mantido à mão e atualizado conforme a necessidade.

| Coluna | Significado |
|---|---|
| `icao` | Código do aeroporto (o mesmo `ident` do OurAirports). |
| `spoken_name` | O nome como deve ser falado depois de "Solo", "Torre", etc. |
| `source` | De onde veio o nome: `vatsim-br` (aparece nos exemplos da VATSIM Brasil), `sugestao` (palpite a confirmar) ou `municipio` (só o município, sem sugestão). |
| `reviewed` | `sim` quando o mantenedor conferiu o nome; `nao` enquanto não. |
| `airport_name` | Nome do aeroporto nos dados, só para ajudar na revisão. |
| `notes` | Observações (colisões de nome, dúvidas). |

**Como atualizar:** edite o `spoken_name`, troque `reviewed` para `sim` e, se quiser, ajuste `source` e `notes`. Para um aeroporto novo, acrescente uma linha. Um aeroporto que não está no arquivo cai no município dos dados, com um aviso no log.

Fontes de conferência: o nome oficial da localidade nas publicações do DECEA (AISWEB) e o que as torres falam de fato.

## `telephony.csv`: telefonia das empresas aéreas

O que o controle diz para o designador de três letras do indicativo ("GLO1234" -> "GOL ..."), e o que o NLU lê quando o piloto fala o nome da empresa. Embutido no `Fonia.Core` (não é lido do disco).

| Coluna | Significado |
|---|---|
| `designator` | O designador ICAO de três letras (`GLO`). |
| `spoken` | O que o controle fala (`GOL`). Pode ter mais de uma palavra (`AIR CANADA`). |
| `registered` | A telefonia registrada, quando difere da falada (`GOL TRANSPORTE`). |
| `country` | País da empresa. |
| `source` | De onde veio: `FAA JO 7340.2N` (capítulo 3, domínio público, que reproduz o ICAO Doc 8585) ou `MCA 100-16`. |
| `notes` | Observações. |

Edição de origem: JO 7340.2N com as mudanças 1 a 3 (17/04/2025). A FAA publica os acréscimos todo mês. Só entram designadores conferidos no registro; veja `docs/decisions/airline-callsigns.md`.
