namespace ClaudeUsageMonitor.Models;

/// <summary>
/// Account and subscription details read from the local Claude Code configuration.
/// Tokens are never read into this model.
/// </summary>
public sealed record AccountInfo
{
    /// <summary>The account email address, from the oauthAccount block.</summary>
    public string? Email { get; init; }

    /// <summary>The full name of the account holder, or the display name when no full name is stored.</summary>
    public string? FullName { get; init; }

    /// <summary>The ID of the organization Claude Code uses, used to pick the same organization for live data.</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The name of the organization that the account belongs to.</summary>
    public string? OrganizationName { get; init; }

    /// <summary>The account's role in its organization.</summary>
    public string? OrganizationRole { get; init; }

    /// <summary>The type of organization that the account belongs to.</summary>
    public string? OrganizationType { get; init; }

    /// <summary>The billing type recorded for the account.</summary>
    public string? BillingType { get; init; }

    /// <summary>The subscription plan name, such as a Pro or Max plan, from the credentials file.</summary>
    public string? SubscriptionType { get; init; }

    /// <summary>Where the subscription type value came from, as recorded in the credentials file.</summary>
    public string? SubscriptionTypeSource { get; init; }

    /// <summary>The rate limit tier of the user, or of the account when the user tier is not stored.</summary>
    public string? RateLimitTier { get; init; }

    /// <summary>The rate limit tier of the organization.</summary>
    public string? OrganizationRateLimitTier { get; init; }

    /// <summary>When the account was created, or null when unknown.</summary>
    public DateTimeOffset? AccountCreatedAt { get; init; }

    /// <summary>When the subscription started, or null when unknown.</summary>
    public DateTimeOffset? SubscriptionCreatedAt { get; init; }

    /// <summary>When the local login credentials expire, or null when unknown.</summary>
    public DateTimeOffset? CredentialsExpireAt { get; init; }

    /// <summary>Whether extra usage is enabled for the account, or null when unknown.</summary>
    public bool? ExtraUsageEnabled { get; init; }
}
