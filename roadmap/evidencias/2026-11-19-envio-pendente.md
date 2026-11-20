## Diagnóstico: marcador `envio.txt` bloqueou instalação do hotfix

**Data:** 2026-11-19, execução `fc26420e0a20417ebb227167c38ed8ce`, plano `fundamentos-csharp-v2`, etapa `01-variaveis`, tentativa 1.

### Fatos
- O script anterior executava `if(Test-Path .../envio.txt){ throw "ENVIO_PENDENTE" }` **antes de sincronizar a correção**. Isso é um defeito do wrapper PS7, não uma falha de compilação.
- O usuário confirmou presença de `envio.txt`.
- Consulta do GitHub nesta rodada: ausentes `roadmap-runs/fc26420e0a20417ebb227167c38ed8ce/01-variaveis/1/Program.cs` e `recibo.json` na branch main.
- O controlador atual grava `envio.txt` antes de chamar `send-chat-message` e `envio.json` apenas após retorno. Portanto o marcador prova intenção de envio, mas não sua entrega; a ausência de candidato remoto não prova falta de envio.

### Correção isolada
- PR #5: `focus-probe` C# reaproveita o preflight de foco sem enviar nem alterar `envio.txt`, registra `focus-probe.jsonl`.
- Recuperador PS7 sincroniza a correção e executa diagnóstico Windows **antes** de avaliar o marcador. Distingue fonte GitHub presente de intenção pendente sem fonte; nenhum reenvio automático se permanece incerto.
- O inventário de janelas usa tarefas, processos e eventos do agendador, somente leitura.

### Critérios de aceite real
1. Corrigir e compilar o controlador em Windows sem apagar `envio.txt`.
2. `ROADMAP_FOCUS_SELFTEST=PASS` e `FOCUS_PROBE=PASS` ou erro determinado com `focus-probe.jsonl`.
3. Coletar `evidencias/diagnostico/janelas-*.json`.
4. Com `envio.txt` e fonte remota ausente, reportar `ESTADO=ENVIO_INCERTO_SEM_CODIGO_REMOTO`; nenhuma chamada `send-chat-message`.
5. Não considerar o fluxo completo até recibo real da etapa no GitHub.

### Próxima dependência
Reconciliar conteúdo do chat/resultado do envio anterior no computador. Somente uma prova de que nenhum envio ocorreu, ou autorização explícita para uma nova tentativa identificada, permite reenviar o prompt sem descartar a segurança contra duplicidade.
