using k8s;
using k8s.Models;

namespace CustomScaler.Services
{
    public class KuberenetesService : IKubernetesService
    {
        private readonly ILogger<KuberenetesService> _logger;
        private readonly IKubernetes _kubernetesClient;
        public KuberenetesService(ILogger<KuberenetesService> logger, IKubernetes kubernetes)
        {
            _logger = logger;
            _kubernetesClient = kubernetes;
        }

        public async Task<string?> CreatePod(V1Pod pod, string @namespace = "default")
        {
            var requestPod = new V1Pod
            {
                ApiVersion = "v1",
                Kind = "Pod",
                Metadata = new V1ObjectMeta
                {
                    Name = $"api-deployment-{Guid.NewGuid().ToString().Substring(0, 10).ToLower()}",
                    Labels = new Dictionary<string, string> { { "app", "hpaapi" } }
                },
                Spec = new V1PodSpec
                {
                    Containers = new List<V1Container>
                        {
                            new V1Container
                            {
                                Name=pod.Spec.Containers[0].Name,
                                Image=pod.Spec.Containers[0].Image,
                                Ports=new List<V1ContainerPort>
                                {
                                    new V1ContainerPort
                                    {
                                        ContainerPort=80,
                                        Name=pod.Spec.Containers[0].Ports[0].Name
                                    }
                                },
                                Resources=new V1ResourceRequirements
                                {
                                    Requests = pod.Spec.Containers[0].Resources.Requests
                                }
                            }
                        }
                }
            };
            var result = await _kubernetesClient.CoreV1.CreateNamespacedPodAsync(requestPod, @namespace);
            if (result != null)
            {
                return result.Name();
            }

            return null;
        }

        public async Task<bool> DeletePod(string podName, string @namespace = "default")
        {

            await _kubernetesClient.CoreV1.DeleteNamespacedPodAsync(podName, @namespace);
            return true;

        }


        public async Task<V1Pod?> GetPod(string podName, string @namespace = "default")
        {

            _logger.LogInformation($"Getting all the pods for a namespace: {@namespace}");
            var podList = await _kubernetesClient.CoreV1.ListNamespacedPodAsync(@namespace);
            if (podList == null || !podList.Items.Any())
            {
                return null;
            }
            var pod = podList.Items.FirstOrDefault(x => x.Name() == podName);
            return pod;

        }

        public async Task<V1PodList?> GetPods(V1Deployment deployment, string @namespace = "default")
        {

            var selector = deployment.Spec.Selector.MatchLabels.TryGetValue("app", out var value) ? value : null;
            if (selector == null)
                return null;

            var podList = await _kubernetesClient.CoreV1.ListNamespacedPodAsync(@namespace, labelSelector: $"app={selector}");
            if (podList == null || !podList.Items.Any())
            {
                return null;
            }
            return podList;

        }

        public async Task<V1Deployment?> GetDeployment(string deploymentName, string @namespace = "default")
        {
            var deployments = await _kubernetesClient.AppsV1.ListNamespacedDeploymentAsync(@namespace);
            return deployments.Items.FirstOrDefault(x => x.Name() == deploymentName);

        }

        public async Task<V1Scale?> PatchReplicaSet(string deploymentName, int? replicas, string @namespace = "default")
        {
            var patchStr = "[{\"op\":\"replace\",\"path\":\"/spec/replicas\",\"value\":" + replicas + "}]";
            var patchRequest = new V1Patch(patchStr, V1Patch.PatchType.JsonPatch);
            return await _kubernetesClient.AppsV1.PatchNamespacedDeploymentScaleAsync(patchRequest, deploymentName, @namespace);
        }


    }
}
