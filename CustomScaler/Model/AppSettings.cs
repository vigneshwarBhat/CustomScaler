namespace CustomScaler.Model
{
    public class Scaling
    {
        public List<HpaTarget> HpaTarget { get; set; }
        public List<VpaTarget> VpaTarget { get; set; }
        
    }
    public class HpaTarget
    {
        public string DeploymentName { get; set; }
        public int MaxReplica { get; set; }
        public int MinReplica { get; set; }
        public string PromoQuery { get; set; }
        public string TargetLimit { get; set; }
    }

    public class VpaTarget
    {
        public string DeploymentName { get; set; }
        public string MaxMemory { get; set; }
        public string PromoQuery { get; set; }
    }
}
