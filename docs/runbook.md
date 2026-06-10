# Operator Runbook

## Common tasks

### Check workflow status

Open the Azure Portal → your Function App → Durable Task → Instances. Filter by instance ID (the `RunId` from the GitHub issue comment or audit log).

Alternatively, query the audit table directly:

```bash
# List all audit events for a run
az storage entity query \
  --account-name <storage-account> \
  --table-name AuditEvents \
  --filter "PartitionKey eq 'run-<id>'"
```

---

### Terminate a stuck workflow

```bash
func durable terminate --id <orchestration-instance-id> --reason "Manual termination by operator"
```

Or via the Azure Portal → Durable Task → Instances → Terminate.

---

### Replay a failed webhook

1. Go to GitHub → repository Settings → Webhooks → recent deliveries.
2. Find the failed delivery and click **Redeliver**.
3. The webhook handler is idempotent — duplicate events for the same issue will start a new orchestration (same `RunId`-based deduplication applies once implemented).

---

### Approve a human review gate

Post a comment on the GitHub issue containing the command:

```
/approve-release
```

The orchestrator is listening for the `ApproveRelease` external event. The GitHubWebhookFunction handles `issue_comment.created` events and raises the event when it detects this command.

The 14-day timeout resets on each new comment — if no approval arrives within 14 days from the last comment, the workflow enters `Failed` state.

---

### Resume a stalled stage

When an LLM agent stage exhausts its automatic retries (e.g. provider quota/credit
exhaustion), the run does not die — it posts an "AI SDLC — Stage Failed (resumable)"
comment and parks. Fix the underlying problem, then post on the issue:

```
/retry
```

The run resumes from the failed stage; earlier stages are not repeated. The retry
window is 7 days, after which the orchestration gives up and ends `Failed`.

---

### Force-fail a workflow

Post a comment on the GitHub issue:

```
/reject-release <reason>
```

This raises a rejection event and the orchestrator transitions to `Failed`.

---

## Environment variables

| Variable | Required | Description |
|----------|----------|-------------|
| `AnthropicApiKey` | Yes | Anthropic API key for Claude |
| `AnthropicModel` | No | Model ID (default: `claude-haiku-4-5-20251001`) |
| `GitHubPat` | Yes | GitHub Personal Access Token (read issues, write comments, manage labels) |
| `GitHubWebhookSecret` | Yes (prod) | HMAC secret configured on the GitHub webhook |
| `AuditStorageAccountName` | Yes | Azure Storage account name for audit table and blob store |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Recommended | Enables Application Insights telemetry |

---

## Local development

See [local-development.md](./local-development.md) for setup instructions.

For local webhook testing, use [ngrok](https://ngrok.com/):

```bash
ngrok http 7071
```

Set the GitHub webhook URL to `https://<your-ngrok-id>.ngrok.io/api/github/webhook`.

Clear `GitHubWebhookSecret` in `local.settings.json` to skip HMAC validation when testing locally. Never commit `local.settings.json`.

---

## Alerts and monitoring

Application Insights is auto-configured when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Key signals to monitor:

- **Failed function executions** — check Failures blade in Application Insights
- **Long-running orchestrations** — query `customEvents` for `OrchestrationStarted` without matching `OrchestrationCompleted` after > 30 minutes
- **High token usage** — monitor `customMetrics` for `input_tokens` / `output_tokens` per agent activity
- **Webhook 401s** — indicates HMAC validation failure; check `GitHubWebhookSecret` matches the GitHub webhook secret

---

## Troubleshooting

### Webhook returns 401

- Verify the GitHub webhook `content_type` is `json` (not `form`).
- Verify `GitHubWebhookSecret` matches exactly (no trailing newline).
- For local development, set `GitHubWebhookSecret` to `""` to skip validation.

### Workflow never starts

- Check the Function App logs for the `GitHubWebhookFunction` trigger.
- Verify the webhook event type is `issues` and the action is `opened` or `reopened`.
- Verify `GitHubPat` has `repo` scope.

### Agent returns empty output

- The `FakeModelProvider` returns deterministic placeholder text; if deployed with `FakeModelProvider`, review DI registration.
- Check Application Insights for Anthropic API errors (4xx = wrong key/model, 5xx = transient).

### Build fails after adding a new persona agent

- Ensure the agent class is added to `AgentActivityFunctions.cs` with a `[Function]`-decorated method.
- Register the agent as `services.AddSingleton<IAgent, YourNewAgent>()` in `Program.cs`.
- Add a constructor call in the `AgentRunner` array in `OrchestratorSkeletonTests.cs`.
