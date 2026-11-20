# Incidente: TARGET_TAB_CHANGED antes do envio — 19/11/2026

## Entrada
Execução: `fc26420e0a20417ebb227167c38ed8ce`, plano `fundamentos-csharp-v2`, chat cadastrado `TEST_ID_0000`.

Trecho observado:
```text
ROADMAP=fundamentos-csharp-v2 EXECUCAO=fc26420e0a20417ebb227167c38ed8ce CHAT=TEST_ID_0000
dotnet: {"status":"BLOCKED","reasonCode":"InvalidOperationException",
"detail":"TARGET_TAB_CHANGED: a URL/identidade da guia não confirmou após o foco."}
```

Nenhum `PEDIDO=` ou `ROADMAP=PASS` foi apresentado. Consulta ao GitHub após a falha não encontrou `roadmap-runs/fc26420e0a20417ebb227167c38ed8ce/01-variaveis/1/Program.cs` nem seu recibo. Isso é compatível com bloqueio no preflight, antes de `envio.txt`; o estado local só pode ser conferido no notebook.

## Causa de tratamento de falha comprovada no código
A versão anterior de `roadmap/src/Program.cs` continha:
```csharp
try { await Ui("focus-chat","--chat",chat); }
catch (IOException) { await Ui("open-chat","--chat",chat); await Ui("focus-chat","--chat",chat); }
```
O `IOException` podia resultar de **qualquer** bloqueio de foco do processo dotnet, inclusive `TARGET_TAB_CHANGED`. O tratamento abria outra guia mesmo sem prova de que o chat estivesse ausente.

O código do runner `ChatAutomation/Browser/EdgeBrowser.cs` na branch `feat/chess-cycle-csharp-20261119` lança a exceção após `ActivateExactTab` falhar em confirmar URL exata e RuntimeId depois de trazer a janela/guia ao primeiro plano. A evidência atual não distingue atraso transitório de UIA de pin realmente divergente; a proteção contra escolher outra guia não deve ser removida.

## Correção entregue
- PR https://github.com/alexbonemberger/chat-automation-test_id_0000/pull/4
- Merge na main: `6fdc092a2ca9f7606fe68b4aab4c710fd745573b`.
- Novo `RoadmapFocusPreflight.cs`: até quatro focos do **mesmo chat cadastrado** quando há instabilidade de identidade. Em `TARGET_TAB_CHANGED`, nenhuma abertura adicional é tentada.
- Para janela fechada ou URL realmente ausente: no máximo uma abertura, seguida obrigatoriamente de `OPENED_VERIFIED`.
- Registra tentativas em `evidencias/roadmap/fundamentos-csharp-v2/01-variaveis/1/focus-preflight.jsonl` antes de qualquer `envio.txt`.
- Script de inventário read-only `roadmap/tools/Inspecionar-Janelas-Agendadas.ps1` preserva lista de tarefas, processos e eventos recentes para identificar janelas CMD/PS7 periódicas sem desabilitar serviços.
- CI Windows https://github.com/alexbonemberger/chat-automation-test_id_0000/actions/runs/44654239561: build .NET 8 PASS, `focus-self-test` PASS, `verifier-self-test` PASS, sintaxe PS7 PASS e regressão legacy PASS.

## Limites da prova
- CI não opera o Edge do notebook; comportamento UIA real e autoria das janelas periódicas continuam pendentes de recibo local.
- Sem `focus-preflight.jsonl` local não se conclui se `TARGET_TAB_CHANGED` deriva de atraso de seleção de guia ou de identidade desatualizada.
- Não repetir `send-chat-message` sem conferir `envio.txt` e código remoto do candidato.

## Aceite do próximo teste real
1. Sincronizar `Program.cs`, `RoadmapFocusPreflight.cs` e a auditoria PS7 no commit de merge acima.
2. Verificar ausência de `envio.txt` e do candidato remoto para `01-variaveis/1`, ou reconciliar antes de retomar.
3. Executar auditoria read-only do scheduler; conferir tarefas com últimos horários correspondentes às janelas.
4. Retomar o mesmo plano/execução. Exigir `PEDIDO=.../01-variaveis/1/Program.cs`, candidato no GitHub, compilação e recibo de Actions antes de declarar PASS.
