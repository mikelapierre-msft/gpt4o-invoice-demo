namespace gpt4o_invoice_demo
{
    internal class Stats
    {
        public int OverallDocuments { get; set; }
        public int OverallPages { get; set; }
        public int OverallInputTokens { get; set; }
        public int OverallOutputTokens { get; set; }
        public int OverallTokens { get; set; }
        public TimeSpan OverallDuration { get; set; }
    }
}
