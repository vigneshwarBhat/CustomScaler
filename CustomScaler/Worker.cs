using CustomScaler.Model;
using CustomScaler.Services;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;
using System.Resources;

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
                    foreach (var target in _options.HpaTarget)
                    {
                        if (target.K8Kind == K8Kind.Deployment)
                        {
                            await ScaleHorizontally(target);
                        }
                        else if (target.K8Kind == K8Kind.Statefulset)
                        {
                            await ScaleHorizontallyStatefulset(target);
                        }
                    }
                    foreach (var target in _options.VpaTarget)
                    {
                        if (target.K8Kind == K8Kind.Deployment)
                        {
                            await ScaledeploymentVertically(target);
                        }
                        else if (target.K8Kind == K8Kind.Statefulset)
                        {
                            await ScaleStatefulsetVertically(target);
                        }
                    }
                    _logger.LogInformation("Worker running ended at: {time}", DateTimeOffset.Now);
                    _logger.LogInformation("--------------------------------------------END -------------------------------------");

                }
                catch (Exception ex)
                {
                    _logger.LogError($"Exception occured :{ex}");
                }
                await Task.Delay(TimeSpan.FromMinutes(_configuration.GetValue<int>("DelayInMin")));
            }
        }

        private async Task ScaleHorizontally(HpaTarget target)
        {
            _logger.LogInformation($"HPA(Deployment): Target deployment: {target.Name}, Min replica:{target.MinReplica}, Max replica:{target.MaxReplica}, HpaTarget:{target.TargetLimit}, Metrics query for scaling: {target.PromoQuery}");
            var deployment = await _kubernetesService.GetDeployment(target.Name);
            if (deployment == null)
            {
                _logger.LogWarning($"HPA(Deployment): target: {target.Name} doesn't exist");
                return;
            }
            //3 pods, with 79, average
            var pods = await _kubernetesService.GetPods(deployment);
            var (averageMemory, averageWithOneLessPod) = await GetAverageMemory(pods, target.PromoQuery, target.ContainerName);
            var limitSetByuser = KubernetesJson.Deserialize<ResourceQuantity>($"\"{target.TargetLimit}\"");
            if (averageMemory >= limitSetByuser && pods?.Items.Count < target.MaxReplica)
            {
                _logger.LogInformation($"HPA(Deployment): Average memory consumbed by {pods.Items.Count} pods is {averageMemory.Value}  which is more than limit {limitSetByuser.Value} set, so new pod  will be created by updating replica.");
                var scale = await _kubernetesService.PatchDeploymentReplicaSet(deployment.Metadata.Name, deployment.Spec.Replicas + 1);
                _logger.LogInformation($"HPA(Deployment): Added new Pod, Increased replic count from {deployment.Spec.Replicas} to {scale?.Spec.Replicas}");
            }
            else if (pods?.Items.Count > target.MinReplica && averageWithOneLessPod < limitSetByuser)
            {
                _logger.LogInformation($"HPA(Deployment): Average memory {averageMemory.Value} is not crossing the limit of {limitSetByuser.Value} but we have more pod's than minimum replica i.e {target.MinReplica}, so {target.Name} will be scaled down.");
                var scale = await _kubernetesService.PatchDeploymentReplicaSet(deployment.Metadata.Name, deployment.Spec.Replicas - 1);
                _logger.LogInformation($"HPA(Deployment): One of the pod deleted, reduced replica to {scale?.Spec.Replicas} from {pods.Items.Count}, since average memory {averageMemory.Value} will not cross the limit of {limitSetByuser.Value} even if we redeuce the pod count by 1.");

            }
            else
            {
                _logger.LogInformation($"HPA(Deployment): No changes in number of replicas for {target.Name} since average memory consumption is {averageMemory.Value}");
            }
        }


        private async Task ScaleHorizontallyStatefulset(HpaTarget target)
        {

            _logger.LogInformation($"HPA(Statefulset): target deployment: {target.Name}, Min replica:{target.MinReplica}, Max replica:{target.MaxReplica}, HpaTarget:{target.TargetLimit}, Metrics query for scaling: {target.PromoQuery}");

            var statefulSet = await _kubernetesService.GetStatefulset(target.Name);
            if (statefulSet == null)
            {
                _logger.LogWarning($"HPA(Statefulset): target: {target.Name} doesn't exist");
                return;
            }
            //3 pods, with 79, average
            var pods = await _kubernetesService.GetStatefullPods(statefulSet);
            var (averageMemory, averageWithOneLessPod) = await GetAverageMemory(pods, target.PromoQuery, target.ContainerName);
            var limitSetByuser = KubernetesJson.Deserialize<ResourceQuantity>($"\"{target.TargetLimit}\"");
            if (averageMemory >= limitSetByuser && pods?.Items.Count < target.MaxReplica)
            {
                _logger.LogInformation($"HPA(Statefulset): Average memory consumbed by {pods.Items.Count} pods is {averageMemory.Value}  which is more than limit {limitSetByuser.Value} set, so new pod  will be created by updating replica.");
                var scale = await _kubernetesService.PatchStatefulsetReplicaSet(statefulSet.Metadata.Name, statefulSet.Spec.Replicas + 1);
                _logger.LogInformation($"HPA(Statefulset): Added new Pod, Increased replic count from {statefulSet.Spec.Replicas} to {scale?.Spec.Replicas}");
                 //await CheckAndCreateService(statefulSet);//creates service for newly created pod, its just to mimic session affinity. In prod we would use ingress with affinity rule we don't need this.

            }
            else if (pods?.Items.Count > target.MinReplica && averageWithOneLessPod < limitSetByuser)
            {
                _logger.LogInformation($"HPA(Statefulset): Average memory {averageMemory.Value} is not crossing the limit of {limitSetByuser.Value} but we have more pod's than minimum replica i.e {target.MinReplica}, And even after we scale down {target.Name} pod, average memory would be around {averageWithOneLessPod}");
                var scale = await _kubernetesService.PatchStatefulsetReplicaSet(statefulSet.Metadata.Name, statefulSet.Spec.Replicas - 1);
                //await _kubernetesService.DeleteService($"{statefulSet.Metadata.Name}-{pods.Items.Count - 1}");
                _logger.LogInformation($"HPA(Statefulset): One of the pod deleted, reduced replica to {scale?.Spec.Replicas} from {pods.Items.Count}, since average memory {averageMemory.Value} will not cross the limit of {limitSetByuser.Value} even if we redeuce the pod count by 1.");

            }
            else
            {
                _logger.LogInformation($"HPA(Statefulset): No changes in number of replicas for {target.Name} since average memory consumption is {averageMemory.Value}");
            }
        }

    
        private async Task ScaledeploymentVertically(VpaTarget target)
        {
            _logger.LogInformation($"VPA(Deployment): target: {target.Name}, MaxMemory:{target.MaxMemory}, metrics for scaling: {target.PromoQuery}");
            var deployment = await _kubernetesService.GetDeployment(target.Name);
            if (deployment == null)
            {
                _logger.LogInformation($"VPA(Deployment): target: {target.Name} doesn't exist");
                return;
            }
            var pods = await _kubernetesService.GetPods(deployment);
            await AvoidScalingVertically(pods, target);

        }

        private async Task ScaleStatefulsetVertically(VpaTarget target)
        {

            _logger.LogInformation($"VPA(Statefulset): target: {target.Name}, MaxMemory:{target.MaxMemory}, metrics for scaling: {target.PromoQuery}");
            var statefulSet = await _kubernetesService.GetStatefulset(target.Name);
            if (statefulSet == null)
            {
                _logger.LogInformation($"VPA(Statefulset):target: {target.Name} doesn't exist");
                return;
            }
            var pods = await _kubernetesService.GetStatefullPods(statefulSet);
            await AvoidScalingVertically(pods, target);

        }

        /*TODO: Need to make it generic, right now, memory/cpu data is getting fetched from kuberentes metrics end point.
            Need to decide based on configuration whether to get from k8s API or prometheuse or always use any one for memory/CPU metrics.
            Using prometheus has a issue for statefulset, since even after pod restart, its holding memory data for previous pod and new pod after restart.
            And not able to identify correct pod which would need to be used since name would be same for statefulset. We can use ID but needs some research*/
        public async Task AvoidScalingVertically(V1PodList? podList, VpaTarget target)
        {
           
            var queries = target.PromoQuery.Split("&&");// TODO: This need to generic, Need to use Utils method.
            var metricsList = await _kubernetesService.GetMetrics();
            if (metricsList == null)
            {
                _logger.LogWarning("Resource usage metrics is not available.");
                return;
            }
            foreach (var pod in podList)
            {
               // var result = await Validate(pod, target);//TODO : Need to work on this.
                var resourceUsed = metricsList.Items.FirstOrDefault(x => x.Metadata.Name == pod.Metadata.Name)?.Containers.FirstOrDefault(c => c.Name == target.ContainerName)?.Usage["memory"];
                if (resourceUsed == null)
                {
                    _logger.LogWarning($"Resource used is null for the pod {pod.Metadata.Name}");
                    continue;
                }
                var largeCartQueryFormatted = string.Format(queries[0], "{machine=\"" + pod.Metadata.Name + "\"}");
                var podLargeCartResponse = await _prometheusService.QueryPrometheus(largeCartQueryFormatted, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow);
                var maxVPALimit = KubernetesJson.Deserialize<ResourceQuantity>($"\"{target.MaxMemory}\"");
                ResourceQuantity? memoryRequested = null;

                //If pod resource request is set in its manifest file
                //And Current pod memory consumption is beyond resource request
                //And no large cart 
                //Delete the pod and expectation would be that new pod will come up post delete, due to min replica criteria.
                //If not we may need to create after deleting. since there is no direct restart API.
                if (pod.Spec.Containers.Any(x => x.Resources.Requests != null
                && x.Resources.Requests.TryGetValue("memory", out memoryRequested)
                && memoryRequested !=null && resourceUsed >= memoryRequested)
                && (podLargeCartResponse == null
                || !podLargeCartResponse.Data.Result.Any()
                || Convert.ToDecimal(podLargeCartResponse.Data.Result[0].values[1].GetString()) <= 0))
                {
                    _logger.LogInformation($"VPA: {pod.Metadata.Name} doesn't have any large cart on it but memory consumed {resourceUsed.Value} has crossed it requested resource {memoryRequested.Value}, so pod will be deleted.");
                    await _kubernetesService.DeletePod(pod.Metadata.Name);

                    _logger.LogInformation($"VPA: Pod {pod.Metadata.Name} got deleted.");
                }
                else if (resourceUsed >= maxVPALimit)
                {
                    await _kubernetesService.DeletePod(pod.Metadata.Name);
                    _logger.LogInformation($"VPA:Memory consumbed by Pod: {resourceUsed.Value}, Max vpa limit of {maxVPALimit.Value} is breached, so pod {pod.Metadata.Name} got deleted.");
                }
                else
                {
                    _logger.LogInformation($"VPA: No changes since memory consuption {resourceUsed.Value} is not more than memory requested {memoryRequested.Value} for the pod {pod.Metadata.Name} or may be large cart on the pod.");
                }
            }

        }

        /// <summary>
        /// Generic method for querying and validating against threshold.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="threshold"></param>
        /// <param name="pod"></param>
        /// <returns></returns>
        private async Task<bool> Validate(V1Pod pod, VpaTarget target)
        {
            var queryList = Util.GetQueryData(target.PromoQuery);
            var valid = true;
            foreach (var (key , val) in queryList)
            {
                var formattedQuery = string.Format(key, "{pod=\"" + pod.Metadata.Name + "\"}");
                var podmemoryResponse = await _prometheusService.QueryPrometheus(formattedQuery, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
                if (podmemoryResponse == null || podmemoryResponse.Data == null || !podmemoryResponse.Data.Result.Any() || podmemoryResponse.Data.Result[0].values.Count < 2)
                {
                    _logger.LogInformation($"VPA: No changes since pod {pod.Name} doesn't have any data available in prometheus. ");
                    valid=valid && true;
                }
                //TODO: We can't just use ">=" blindly, instead we need to use what is been defined in appsettings.
                if (Convert.ToDecimal(podmemoryResponse.Data.Result[0].values[1].GetString()) >= val)
                    valid = valid && false;

                valid = valid && true;
            }
            return valid;  
        }


        /*TODO: Need to make it generic, right now, memory/cpu data is getting fetched from kuberentes metrics end point.
         Need to decide based on configuration whether to get from k8s API or prometheuse or always use any one for memory/CPU metrics.
         Using prometheus has a issue for statefulset, since even after pod restart, its holding memory data for previous pod and new pod after restart.
         And not able to identify correct pod which would need to be used since name would be same for statefulset. We can use ID but needs some research*/
        private async Task<Tuple<ResourceQuantity, ResourceQuantity>?> GetAverageMemory(V1PodList? podList, string query, string containerName)
        {
            //decimal totalMemory = 0;
            if (podList == null)
                return null;
            var metricsList = await _kubernetesService.GetMetrics();
            if(metricsList == null)
            {
                _logger.LogWarning("Resource usage metrics is not available.");
                return null;
            }
            ResourceQuantity? totalResourceUsed = null;
            foreach (var pod in podList)
            {
                var resourceUsed= metricsList.Items.FirstOrDefault(x=> x.Metadata.Name == pod.Metadata.Name)?.Containers.FirstOrDefault(c=>c.Name== containerName)?.Usage["memory"];
                if (resourceUsed == null)
                { 
                    continue; 
                }
                totalResourceUsed += resourceUsed;
            }

            var averageMemory = totalResourceUsed / podList.Items.Count; //totalMemory / podList.Items.Count;
            var averageMemoryWithOneLessPod = totalResourceUsed / (podList.Items.Count-1); //totalMemory / (podList.Items.Count - 1);
            //return Tuple.Create(averageMemory, averageMemoryWithOneLessPod);
            return Tuple.Create(KubernetesJson.Deserialize<ResourceQuantity>($"\"{averageMemory}\""), KubernetesJson.Deserialize<ResourceQuantity>($"\"{averageMemoryWithOneLessPod}\""));
        }

    }
}