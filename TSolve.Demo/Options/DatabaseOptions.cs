namespace TSolve.Demo.Options;

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public string? Url { get; set; }
    public bool MigrateOnStartup { get; set; } = true;
}
