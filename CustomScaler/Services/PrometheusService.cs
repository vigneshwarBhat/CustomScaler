using CustomScaler.Model;
using System.Text.Json;

namespace CustomScaler.Services
{
    public class PrometheusService : IPrometheusService
    {
        private readonly ILogger<PrometheusService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        public PrometheusService(ILogger<PrometheusService> logger, HttpClient httpClient, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;

        }
        public async Task<PrometheusResponse?> QueryPrometheus(string query, DateTime start, DateTime end)
        {
            var urlEncodedeRequest = new FormUrlEncodedContent(
                new List<KeyValuePair<string, string>>
                {
                       new KeyValuePair<string, string>("query", query)
                });
            var result = await _httpClient.PostAsync($"{_configuration.GetValue<string>("PrometheusServer")}/query", urlEncodedeRequest);
            if (result.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<PrometheusResponse>(await result.Content.ReadAsStringAsync());
            }
            return null;

        }
    }
}
