using CustomScaler.Model;
using CustomScaler.Services;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace CustomScaler
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IPrometheusService _prometheusService;
        private readonly IKubernetesService _kubernetesService;
        private readonly Scaling _options;
        public Worker(ILogger<Worker> logger, IConfiguration configuration, IPrometheusService prometheusService, IKubernetesService kubernetesService, IOptions<Scaling> options)
        {
            _logger = logger;
            _configuration = configuration;
            _prometheusService = prometheusService;
            _kubernetesService = kubernetesService;
            _options = options.Value;
        }

        /// <summary>
        /// Run this as cron job 
        /// Need minor change for supporting cron
        /// https://medium.com/@gtaposh/net-core-3-1-cron-jobs-background-service-e3026047b26d
        /// https://codeburst.io/schedule-cron-jobs-using-hostedservice-in-asp-net-core-e17c47ba06
        /// Configure it to run may be evry 5 min once
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                    await ScaleHorizontally();
                    await ScalingVertically();
                    _logger.LogInformation("Worker running ended at: {time}", DateTimeOffset.Now);
                    _logger.LogInformation("--------------------------------------------END -------------------------------------");

                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception occured :{ex}");
                }
                await Task.Delay(60000);
            }
        }

        public async Task ScaleHorizontally()
        {
            foreach (var target in _options.HpaTarget)
            {
                _logger.LogInformation($"HPA target deployment: {target.DeploymentName}, Min replica:{target.MinReplica}, Max replica:{target.MaxReplica}, HpaTarget:{target.TargetLimit}, Metrics query for scaling: {target.PromoQuery}");
                var deployment = await _kubernetesService.GetDeployment(target.DeploymentName);
                if (deployment == null)
                {
                    _logger.LogWarning($"HPA target: {target.DeploymentName} doesn't exist");
                    return;
                }
                //3 pods, with 79, average
                var pods = await _kubernetesService.GetPods(deployment);
                var (averageMemory, averageWithOneLessPod) = await GetAverageMemory(pods, target.PromoQuery);
                var limitSetByuser = KubernetesJson.Deserialize<ResourceQuantity>($"\"{target.TargetLimit}\"");
                if (averageMemory >= limitSetByuser && pods?.Items.Count < target.MaxReplica)
                {
                    _logger.LogInformation($"HPA: Average memory consumbed by {pods.Items.Count} pods is {averageMemory.Value}  which is more than limit {limitSetByuser.Value} set, so new pod  will be created by updating replica.");
                    var scale = await _kubernetesService.PatchReplicaSet(deployment.Metadata.Name, deployment.Spec.Replicas + 1);
                    _logger.LogInformation($"HPA: Added new Pod, Increased replic count from {deployment.Spec.Replicas} to {scale?.Spec.Replicas}");
                }
                else if (pods?.Items.Count > target.MinReplica && averageWithOneLessPod < limitSetByuser)
                {
                    _logger.LogInformation($"HPA: Average memory {averageMemory.Value} is not crossing the limit of {limitSetByuser.Value} but we have more pod's than minimum replica i.e {target.MinReplica}, so {target.DeploymentName} will be scaled down.");
                    var scale = await _kubernetesService.PatchReplicaSet(deployment.Metadata.Name, deployment.Spec.Replicas - 1);
                    _logger.LogInformation($"HPA: One of the pod deleted, reduced replica to {scale?.Spec.Replicas} from {pods.Items.Count}, since average memory {averageMemory.Value} will not cross the limit of {limitSetByuser.Value} even if we redeuce the pod count by 1.");

                }
                else
                {
                    _logger.LogInformation($"HPA: No changes in number of replicas for {target.DeploymentName} since average memory consumption is {averageMemory.Value}");
                }
            }
        }

        public async Task ScalingVertically()
        {
            foreach (var target in _options.VpaTarget)
            {
                _logger.LogInformation($"VPA: Vpa target: {target.DeploymentName}, MaxMemory:{target.MaxMemory}, metrics for scaling: {target.PromoQuery}");
                var deployment = await _kubernetesService.GetDeployment(target.DeploymentName);
                if (deployment == null)
                {
                    _logger.LogInformation($"VPA:VPA target: {target.DeploymentName} doesn't exist");
                    return;
                }
                var pods = await _kubernetesService.GetPods(deployment);
                await AvoidScalingVertically(pods, target);
            }
        }

        public async Task AvoidScalingVertically(V1PodList? podList, VpaTarget target)
        {
            var queries = target.PromoQuery.Split("&&");
            foreach (var pod in podList)
            {
                var formattedMemoryQuery = string.Format(queries[1], "{pod=\"" + pod.Metadata.Name + "\"}");
                var podmemoryResponse = await _prometheusService.QueryPrometheus(formattedMemoryQuery, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                var largeCartQueryFormatted = string.Format(queries[0], "{machine=\"" + pod.Metadata.Name + "\"}");
                var podLargeCartResponse = await _prometheusService.QueryPrometheus(largeCartQueryFormatted, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                var memoryConsumedByPod = KubernetesJson.Deserialize<ResourceQuantity>($"\"{Convert.ToDecimal(podmemoryResponse.Data.Result[0].values[1].GetString())}\"");
                var maxVPALimit = KubernetesJson.Deserialize<ResourceQuantity>($"\"{target.MaxMemory}\"");
                ResourceQuantity? memoryRequested = null; 
                //If pod resource request is set in its manifest file
                //And Current pod memory consumption is beyond resource request
                //And no large cart 
                //Delete the pod and expectation would be that new pod will come up post delete, due to min replica criteria.
                //If not we may need to create after deleting. since there is no direct restart API.
                if (pod.Spec.Containers.Any(x => x.Resources.Requests != null 
                && x.Resources.Requests.TryGetValue("memory", out memoryRequested)
                && memoryConsumedByPod >= memoryRequested)
                && (podLargeCartResponse == null
                || !podLargeCartResponse.Data.Result.Any() 
                || Convert.ToDecimal(podLargeCartResponse.Data.Result[0].values[1].GetString()) <= 0))
                {
                    _logger.LogInformation($"VPA: {pod.Metadata.Name} doesn't have any large cart on it but memory consumed {memoryConsumedByPod.Value} has crossed it requested resource {memoryRequested.Value}, so pod will be deleted.");
                    await _kubernetesService.DeletePod(pod.Metadata.Name);
                    _logger.LogInformation($"VPA: Pod {pod.Metadata.Name} got deleted.");
                }
                else if(memoryConsumedByPod >= maxVPALimit)
                {
                    await _kubernetesService.DeletePod(pod.Metadata.Name);
                    _logger.LogInformation($"VPA:Memory consumbed by Pod: {memoryConsumedByPod.Value}, Max vpa limit of {maxVPALimit.Value} is breached, so pod {pod.Metadata.Name} got deleted.");                   
                }
                else
                {
                    _logger.LogInformation($"VPA: No changes since memory consuption {memoryConsumedByPod.Value} is not more than memory requested {memoryRequested.Value} for the pod {pod.Metadata.Name}");
                }
            }

        }

        private async Task <Tuple<ResourceQuantity, ResourceQuantity>?> GetAverageMemory(V1PodList? podList, string query)
        {
            decimal totalMemory = 0;
            if (podList == null)
                return null;

            foreach (var pod in podList)
            {
                var formattedMemoryQuery = string.Format(query, "{pod=\"" + pod.Metadata.Name + "\"}");
                var podmemoryResponse = await _prometheusService.QueryPrometheus(formattedMemoryQuery, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);
                if (podmemoryResponse?.Data != null)
                {
                    totalMemory += Convert.ToDecimal(podmemoryResponse.Data.Result[0].values[1].GetString());
                }
            }
            var averageMemory = totalMemory / podList.Items.Count;
            var averageMemoryWithOneLessPod = totalMemory / (podList.Items.Count-1);
            return Tuple.Create(KubernetesJson.Deserialize<ResourceQuantity>($"\"{averageMemory}\""), KubernetesJson.Deserialize<ResourceQuantity>($"\"{averageMemoryWithOneLessPod}\""));
        }

    }
}