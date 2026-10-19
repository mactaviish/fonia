# Dados

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
