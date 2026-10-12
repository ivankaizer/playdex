namespace Api.Modules.Posters;

public sealed class PostersOptions
{
    // Public origin of this API (e.g. https://dash.playdex.dev); stored poster URLs point here.
    public string? PublicBaseUrl { get; set; }
    public string WeModBaseUrl { get; set; } = "https://api-cdn.wemod.com";
    public R2Options R2 { get; set; } = new();

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(PublicBaseUrl) &&
        !string.IsNullOrWhiteSpace(R2.AccountId) &&
        !string.IsNullOrWhiteSpace(R2.AccessKeyId) &&
        !string.IsNullOrWhiteSpace(R2.SecretAccessKey) &&
        !string.IsNullOrWhiteSpace(R2.Bucket);
}

public sealed class R2Options
{
    public string? AccountId { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? Bucket { get; set; }
}
