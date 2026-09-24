namespace TSolve.Demo.Options;

public sealed class PipelineOptions
{
    public const string Section = "Pipeline";

    public int CandidateQualityThreshold { get; set; } = 55;
    public int WeakResolutionMinTokens { get; set; } = 4;
    public double PublishedMatchThreshold { get; set; } = 0.18;
    public double NearDuplicateThreshold { get; set; } = 0.75;
    public double NearDuplicateTitleThreshold { get; set; } = 0.45;
    public double ClusterSimilarityThreshold { get; set; } = 0.35;
    public double SynthesisConsensusThreshold { get; set; } = 0.45;
    public double SynthesisOutlierThreshold { get; set; } = 0.30;
    public int PromotionMinEvidence { get; set; } = 3;
    public int HighRiskPromotionMinEvidence { get; set; } = 2;
    public int PromotionMinAverageQuality { get; set; } = 55;
    public int BulkPromotionLimit { get; set; } = 24;
    public int DailyPromotionLimit { get; set; } = 3;
    public string[] HighRiskKeywords { get; set; } = ["production", "prod", "payroll", "finance", "maternity", "policy", "admin", "security"];
    public string[] MediumRiskKeywords { get; set; } = ["access", "permission", "laptop", "device", "invoice"];
}

public sealed class SynthesisOptions
{
    public const string Section = "Synthesis";
    public string Provider { get; set; } = "Internal";
    public string Model { get; set; } = "internal-safe-rewriter-v1";
    public string? Endpoint { get; set; }
    public string ApiKeyEnvironmentVariable { get; set; } = "TSOLVE_LLM_API_KEY";
    public bool AllowExternalForSensitiveContent { get; set; }
}
