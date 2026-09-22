namespace TSolve.Demo.Options;

public sealed class PipelineOptions
{
    public const string Section = "Pipeline";

    public int CandidateQualityThreshold { get; set; } = 55;
    public int WeakResolutionMinTokens { get; set; } = 4;
    public double PublishedMatchThreshold { get; set; } = 0.18;
    public double NearDuplicateThreshold { get; set; } = 0.75;
    public double NearDuplicateTitleThreshold { get; set; } = 0.45;
    public double ClusterSimilarityThreshold { get; set; } = 0.52;
    public int PromotionMinEvidence { get; set; } = 3;
    public int HighRiskPromotionMinEvidence { get; set; } = 2;
    public int PromotionMinAverageQuality { get; set; } = 55;
    public int BulkPromotionLimit { get; set; } = 24;
    public int DailyPromotionLimit { get; set; } = 3;
    public string[] HighRiskKeywords { get; set; } = ["production", "prod", "payroll", "finance", "maternity", "policy", "admin", "security"];
    public string[] MediumRiskKeywords { get; set; } = ["access", "permission", "laptop", "device", "invoice"];
}
