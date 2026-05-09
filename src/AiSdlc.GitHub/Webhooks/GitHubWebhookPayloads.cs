using System.Text.Json.Serialization;

namespace AiSdlc.GitHub.Webhooks;

// Minimal payload models for the GitHub webhook events the platform handles.
// Only fields actively used by the webhook handler are mapped.

public sealed record IssueWebhookPayload
{
    [JsonPropertyName("action")]   public string Action     { get; init; } = string.Empty;
    [JsonPropertyName("issue")]    public WebhookIssue Issue { get; init; } = new();
    [JsonPropertyName("repository")] public WebhookRepository Repository { get; init; } = new();
}

public sealed record IssueCommentWebhookPayload
{
    [JsonPropertyName("action")]   public string Action     { get; init; } = string.Empty;
    [JsonPropertyName("issue")]    public WebhookIssue Issue { get; init; } = new();
    [JsonPropertyName("comment")]  public WebhookComment Comment { get; init; } = new();
    [JsonPropertyName("repository")] public WebhookRepository Repository { get; init; } = new();
}

public sealed record PullRequestWebhookPayload
{
    [JsonPropertyName("action")]       public string Action      { get; init; } = string.Empty;
    [JsonPropertyName("number")]       public int Number         { get; init; }
    [JsonPropertyName("pull_request")] public WebhookPullRequest PullRequest { get; init; } = new();
    [JsonPropertyName("repository")]   public WebhookRepository Repository   { get; init; } = new();
}

public sealed record WebhookIssue
{
    [JsonPropertyName("number")]   public int Number    { get; init; }
    [JsonPropertyName("title")]    public string Title  { get; init; } = string.Empty;
    [JsonPropertyName("body")]     public string? Body  { get; init; }
    [JsonPropertyName("html_url")] public string Url    { get; init; } = string.Empty;
    [JsonPropertyName("user")]     public WebhookUser User { get; init; } = new();
}

public sealed record WebhookComment
{
    [JsonPropertyName("id")]         public long Id           { get; init; }
    [JsonPropertyName("body")]       public string Body       { get; init; } = string.Empty;
    [JsonPropertyName("user")]       public WebhookUser User  { get; init; } = new();
    [JsonPropertyName("html_url")]   public string Url        { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; init; }
}

public sealed record WebhookPullRequest
{
    [JsonPropertyName("number")]   public int Number    { get; init; }
    [JsonPropertyName("title")]    public string Title  { get; init; } = string.Empty;
    [JsonPropertyName("html_url")] public string Url    { get; init; } = string.Empty;
    [JsonPropertyName("head")]     public WebhookBranch Head { get; init; } = new();
    [JsonPropertyName("base")]     public WebhookBranch Base { get; init; } = new();
}

public sealed record WebhookRepository
{
    [JsonPropertyName("full_name")] public string FullName { get; init; } = string.Empty;
}

public sealed record WebhookUser
{
    [JsonPropertyName("login")] public string Login { get; init; } = string.Empty;
}

public sealed record WebhookBranch
{
    [JsonPropertyName("ref")] public string Ref { get; init; } = string.Empty;
}
