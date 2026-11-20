# Evidência auditável — roadmap inicial — 2026-11-19

## Identificação
- Repositório: `alexbonemberger/chat-automation-test_id_0000`
- Branch: `main`
- Execução local reportada: `69b8c98851f74ceb83e14c1bec739ebf`
- Comando reportado: `ROADMAP=PASS`
- Executável do controlador: C#/.NET 8 (`roadmap/src/Program.cs`), compilado no Windows conforme saída PS7 fornecida.
- Natureza da auditoria: leitura posterior dos arquivos e recibos do GitHub; não é uma reexecução do processo Windows nesta sessão.

## Prova do fluxo com chat real (conforme saída da execução + GitHub)
| Etapa | Tentativa | Código (commit) | SHA-256 da fonte informado no recibo | Testes | GitHub Actions |
|---|---|---|---|---|---|
| dobro | 1 | `40071f40a0cb4d6aee33eb8b276915c8ec2171bb` | `FF46A07B5262612126898883AAFA518D986781BC0C8351B85DB15C973EFB342E` | 4→8; -7→-14; PASS | https://github.com/alexbonemberger/chat-automation-test_id_0000/actions/runs/44642034168 |
| triplo | 1 | `2941caa056dab47fcb201c0ed45e15987086d915` | `00A2470FF32B130C771A5258AAC6783C0F0D541DCE16CDC3155018DB8545DDB9` | 5→15; -2→-6; PASS | https://github.com/alexbonemberger/chat-automation-test_id_0000/actions/runs/44642260497 |

Recibos (conteúdo preservado):
- https://github.com/alexbonemberger/chat-automation-test_id_0000/blob/main/roadmap-runs/69b8c98851f74ceb83e14c1bec739ebf/dobro/1/recibo.json
- https://github.com/alexbonemberger/chat-automation-test_id_0000/blob/main/roadmap-runs/69b8c98851f74ceb83e14c1bec739ebf/triplo/1/recibo.json

Confirmação separada de escrita em `responses/0003.md`:
- https://github.com/alexbonemberger/chat-automation-test_id_0000/blob/main/responses/0003.md
- Commit `2a3e6fcf663412f262cb44c2eeb195f867e9a946`.

## Prova anterior do simulador (NÃO confundir com chat real)
Execução `17939044cc284863a14195362566664a`, com candidatos fornecidos por substituto do Edge:
- `dobro/1`: FAIL, C# `CS1525`, `Program.cs(1,9)`, diagnosticado em `buildLog`.
- `dobro/2`: PASS, compilação e 2 testes.
- `triplo/1`: PASS, compilação e 2 testes.

## Lacunas ainda abertas
1. O verificador de candidato local invoca `roadmap/verificar.ps1`, não o C# diretamente.
2. Ao alcançar `isMaxLimit` antes de novo envio, o controlador atual encerra com falha, ainda sem reconciliação da nova rota.
3. Uma intenção de envio gravada antes do retorno UIA pode ficar ambígua após interrupção; não reenviar sem evidência.
4. Testes do roadmap `inicial` não incluem regressão acumulada.
5. Esta auditoria não prova execução de projetos Unity/Qwen nem recuperação end-to-end do handoff.

## Próximo incremento
- Plano `fundamentos-csharp-v2`: oito features isoladas, 20 casos acumulados na última etapa (95 verificações totais), um candidato por prompt e limite de 3 tentativas por etapa.
- URL: https://github.com/alexbonemberger/chat-automation-test_id_0000/blob/main/roadmap/plans/fundamentos-csharp-v2.json
- Precondição para afirmar `PASS`: logs de compilação, testes do Actions e recibos de cada etapa, com SHA-256 da fonte e commit.
- Todo estado `UNKNOWN` ou UIA inconclusivo deve bloquear reenvio automático.
