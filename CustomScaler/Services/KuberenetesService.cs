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

        public async Task<V1PodList?> GetStatefullPods(V1StatefulSet statefulSet, string @namespace = "default")
        {

            var selector = statefulSet.Spec.Selector.MatchLabels.TryGetValue("app", out var value) ? value : null;
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

        public async Task<V1StatefulSet?> GetStatefulset(string name, string @namespace = "default")
        {
            var deployments = await _kubernetesClient.AppsV1.ListNamespacedStatefulSetAsync(@namespace);
            return deployments.Items.FirstOrDefault(x => x.Name() == name);

        }

        public async Task<V1Scale?> PatchDeploymentReplicaSet(string deploymentName, int? replicas, string @namespace = "default")
        {
            var patchStr = "[{\"op\":\"replace\",\"path\":\"/spec/replicas\",\"value\":" + replicas + "}]";
            var patchRequest = new V1Patch(patchStr, V1Patch.PatchType.JsonPatch);
            return await _kubernetesClient.AppsV1.PatchNamespacedDeploymentScaleAsync(patchRequest, deploymentName, @namespace);
        }

        public async Task<V1Scale?> PatchStatefulsetReplicaSet(string deploymentName, int? replicas, string @namespace = "default")
        {
            var patchStr = "[{\"op\":\"replace\",\"path\":\"/spec/replicas\",\"value\":" + replicas + "}]";
            var patchRequest = new V1Patch(patchStr, V1Patch.PatchType.JsonPatch);
            return await _kubernetesClient.AppsV1.PatchNamespacedStatefulSetScaleAsync(patchRequest, deploymentName, @namespace);
        }

        public async Task<V1Service?> CreateNewService(V1Service service, string Name, string @namespace = "default")
        {
            var v1Service = new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = Name,
                    Annotations = service.Metadata.Annotations
                },
                Spec = new V1ServiceSpec
                {
                    Type = service.Spec.Type,
                    ExternalTrafficPolicy = service.Spec.ExternalTrafficPolicy,
                    Selector = new Dictionary<string, string> { { "statefulset.kubernetes.io/pod-name", Name } },
                    Ports = new V1ServicePort[]
                    {
                        new V1ServicePort
                        {
                            Protocol="TCP",
                            Port=80,
                            TargetPort=service.Spec.Ports[0].TargetPort,
                            NodePort=service.Spec.Ports[0].NodePort+1
                        }
                    }
                }
            };

            return await _kubernetesClient.CoreV1.CreateNamespacedServiceAsync(v1Service, @namespace);

        }

        public async Task<V1Service?> GetService(string serviceName, string @namespace = "default")
        {
            try
            {
                var services = await _kubernetesClient.CoreV1.ReadNamespacedServiceAsync(serviceName, @namespace);
                return services;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, ex);
                return null;
            }
        }

        public async Task<V1Service?> DeleteService(string serviceName, string @namespace = "default")
        {
            var services = await _kubernetesClient.CoreV1.DeleteNamespacedServiceAsync(serviceName, @namespace);
            return services;
        }

        public async Task<V1ServiceList?> GetServices(string selector, string @namespace = "default")
        {
            try
            {
                var services = await _kubernetesClient.CoreV1.ListNamespacedServiceAsync(@namespace, labelSelector: $"app={selector}");
                return services;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, ex);
                return null;
            }
        }

        public async Task<PodMetricsList?> GetMetrics(string @namespace = "default")
        {
           var metricsList = await _kubernetesClient.GetKubernetesPodsMetricsByNamespaceAsync(@namespace);
            return metricsList;
        }

    }
}
