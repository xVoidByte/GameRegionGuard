namespace GameRegionGuard.Models
{
    public class InstallationResult
    {
        public int TotalRules { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool WasCancelled { get; set; }
    }
}