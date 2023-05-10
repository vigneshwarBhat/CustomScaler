using CustomScaler.Model;


namespace CustomScaler.Services
{
    public interface IPrometheusService
    {
        Task<PrometheusResponse?> QueryPrometheus(string query, DateTime start, DateTime end);
    }
}
