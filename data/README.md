# Dados

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
