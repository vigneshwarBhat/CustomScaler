using System.Text.Json.Serialization;

namespace CustomScaler.Model
{
    public class PrometheusResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("data")]
        public Data Data { get; set; }

    }
    public class Data
    {
        [JsonPropertyName("resultType")]
        public string ResultType { get; set; }

        [JsonPropertyName("result")]
        public List<Result> Result { get; set; }
    }

    public class Metric
    {
        [JsonPropertyName("pod")]
        public string Pod { get; set; }
    }

    public class Result
    {
        [JsonPropertyName("metric")]
        public Metric Metric { get; set; }

        [JsonPropertyName("value")]
        public List<dynamic> values { get; set; }
    }
}
