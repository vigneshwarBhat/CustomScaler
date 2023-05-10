using CustomScaler.Model;
using k8s.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomScaler.Services
{
    public interface IKubernetesService
    {
        Task<V1Pod?> GetPod(string podName, string @namespace= "default");

        Task<V1PodList?> GetPods(V1Deployment deployment, string @namespace = "default");
        Task<string?> CreatePod(V1Pod pod, string @namespace = "default");
        Task<bool> DeletePod(string podName, string @namespace = "default");

        Task<V1Deployment?> GetDeployment(string deploymentName, string @namespace = "default");
        Task<V1Scale?> PatchReplicaSet(string deploymentName, int? replicas, string @namespace = "default");
    }
}
