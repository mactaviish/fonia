# Frequências de Controle e Aproximação (pesquisa do M5)

Pesquisa feita em 2026-10-18 para o item "Aproximação / Centro e handoffs" do M5. Responde: quem presta o controle de aproximação no Brasil, o que os dados abertos têm, o que eles não têm e o que o Fonia faz com isso.

## 1. O que o manual e a ICA dizem

- **Art. 127 do MCA 100-16 (2025):** a instrução depois da decolagem tem indicativo, instruções complementares, hora da decolagem e **próximo órgão e frequência**. O exemplo é "TAM 3661, decolado aos 26, chame o Controle Rio, 119,0". Outro exemplo manda chamar o "Controle Rio 119,0" depois de interceptar uma radial.
- **ICA 100-37, Art. 34:** o Serviço de Controle de Aproximação é prestado por (I) um **APP** ou (II) um **ACC ou uma TWR** a que isso tenha sido delegado, dentro de um espaço aéreo. O parágrafo único diz que (II) ocorre quando convém juntar o controle de aproximação com o de área ou o de aeródromo num só órgão.
- **ICA 100-37, Art. 33 e 35:** o controle de área é do ACC (ou de um APP com delegação); o controle de aeródromo é da TWR.
- **Nomes falados (MCA):** o APP se chama "Controle (localidade)" ("Controle Rio", "Controle Recife"); o ACC aparece como "Centro (localidade)" ("Centro Recife", 127,2). O Art. 127 chama "Controle" o órgão seguinte de quem decola de um aeródromo com APP.

**Consequência:** a ausência de um APP no cadastro **não** quer dizer que ninguém presta controle de aproximação. Pode ser a própria TWR (delegação) ou o ACC. Quem é, em cada aeródromo, é uma delegação publicada no AIP/ROTAER, e não sai de regra geral.

## 2. O que os dados abertos têm (OurAirports, `data/ourairports`)

| | Quantidade |
|---|---|
| Aeródromos | 5.722 |
| Com alguma frequência | 164 |
| Com `APP` | 59 |
| Com `TWR` | 61 (9 sem `APP`) |
| Grandes (`large_airport`) | 28 (3 sem `APP`: SBGO, SBPS, SBSG) |

Tipos das frequências: AFIS 79, APP 65, TWR 62, GND 34, ATIS 28, OPS 23, RDO 12, CLD 8, ILS 7, outros menores.

- **Aeroportos com TWR e sem APP nos dados:** SBGO, SBIH, SBMG, SBPS, SBSG, SBTA, SDCO, SDMC, SNOZ. Para SBGO, SBPS e SBSG a lacuna é provavelmente do cadastro, não necessariamente do serviço; não dá para afirmar sem a fonte oficial.
- **Tipos fora do padrão:** `PMSV` (SBBR), `Free`, `Radio`, `MULTICOM`, `Frequência livre`, `Guarapuava RDO` e dois de SDUL: `APP ACADEMIA` ("Controle Academia", 119,75) e `CTR Ribeirão` ("Torre Ribeirão", 118). O `AirportCatalog` compara o tipo sem diferenciar maiúsculas e minúsculas (então `Afis` é lido como AFIS), mas só reconhece os nomes exatos: `APP ACADEMIA` e os demais caem em `Other`. Quem sintoniza a "Controle Academia" de SDUL hoje ouve "ninguém responde".
- **Sem informação de saída:** não existe tipo "saída". A frequência de saída que a Torre passa depois da decolagem é a do órgão seguinte, que é o APP quando há um.

## 3. Fontes oficiais e a licença

- A fonte de verdade é a **AISWEB / ROTAER / AIP-Brasil** do DECEA. A AISWEB tem API, com cadastro e chave. Os termos da API são os da **Política de Uso do AISWEB** (o mesmo documento do site).
- **Política de Uso (cópia local em `docs/fontes-locais`, fora do git):**
  - §10: todo uso do conteúdo é proibido, salvo as autorizações da própria política. Para dados, a única autorização é para **quem tem acesso autorizado à API**: incluir os dados em base própria, compartilhá-los por meios eletrônicos e distribuí-los pela Internet, de forma customizada.
  - §13, b: usar robôs, crawlers ou ferramentas de coleta e extração, ou impor carga excessiva, é violação. Nada de raspar páginas.
  - §4: a API pode ser cobrada em uso comercial; o site é ferramenta de planejamento, e o COMAER se isenta de danos.
  - §12: os direitos autorais são do DECEA. O selo CC BY 4.0 no rodapé do site contradiz o texto; vale a política, que é de cumprimento obrigatório.
- O princípio 6 do `CLAUDE.md` pede dados com licença aberta. Os da AISWEB têm licença própria, concedida só a quem usa a API. **Decisão:** nenhum dado da AISWEB (CSV, código ou texto copiado) entra no repositório até as condições abaixo estarem resolvidas.
- **Condições para embutir uma tabela de frequências derivada da API** (gerada pelo mantenedor, com a chave dele, por script em `tools/`, poucas chamadas):
  1. Confirmar com o DECEA, por escrito, que terceiros podem receber e usar a tabela distribuída com o Fonia, e se isso conta como uso comercial (§4).
  2. O mantenedor atualizar o princípio 6 do `CLAUDE.md` para admitir dados com licença própria e permissão expressa.
  3. Arquivo separado (`data/aisweb/`), com atribuição ao DECEA no `NOTICE`, aviso de que não serve para navegação real e rotina de atualização (ciclo AIRAC).
- Alternativa sem depender do item 1: cada usuário usa a própria chave da API, com cache local e retorno ao OurAirports quando faltar.

## 4. O que fazemos com isso

1. **Handoff depois da decolagem:** só quando os dados têm um `APP` do aeródromo, a Torre diz "chame o Controle (nome) (frequência)". Sem `APP`, fica o "decolado aos MM" de hoje. Não inventamos o Centro, porque quem recebe o voo é uma delegação que não temos.
2. **Chegada sem `APP`:** continua com a Torre, que o Art. 34 permite quando há delegação, e é o que o app já faz.
3. **Corrigir a leitura dos tipos:** tratar como aproximação os tipos que contêm `APP` (como `APP ACADEMIA`). Pequeno e vale um teste. Um aeroporto pode ter mais de um `APP`; a frequência de saída fica com a primeira, e o `FrequencyResolver` reconhece qualquer uma das duas.
4. **Complemento por configuração** (frequência de Controle ou Centro por aeroporto em `appsettings.json`) fica fora desta fase. Quem quiser usa o AIP por conta própria; a decisão sobre o produto é do mantenedor.
5. **Continua em aberto:** a formulação da Torre quando o voo vai para o Centro sem APP. É `TODO(fraseologia)` até haver referência.

## 5. Fontes

- MCA 100-16 (edição 2025), Art. 127, 129 e as seções de vetoração e de espaço aéreo G (PDF local, `docs/fontes-locais`).
- ICA 100-37, Art. 28 a 38 (compilado, PDF local).
- OurAirports (domínio público), arquivos `br-airports.csv` e `br-airport-frequencies.csv`.
- AISWEB/DECEA e discussões do OpenStreetMap sobre o ROTAER, apenas para a questão da licença.
