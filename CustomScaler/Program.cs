using CustomScaler;
using CustomScaler.Model;
using CustomScaler.Services;
using k8s;
using Serilog;


var host = Host.CreateDefaultBuilder(args).
    ConfigureAppConfiguration(
        (hostContext, config) =>
        {
            config.SetBasePath(Directory.GetCurrentDirectory());
            config.AddJsonFile("appsettings.json", false, true);
            config.AddEnvironmentVariables();
        }
    )
    .ConfigureLogging(
        loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            loggingBuilder.AddSerilog(logger, dispose: true);
        }
    )
    .ConfigureServices(services =>
    {
        var provider = services.BuildServiceProvider();
        var config = provider
            .GetRequiredService<IConfiguration>();
        // Load kubernetes configuration
        var kubernetesClientConfig = KubernetesClientConfiguration.BuildDefaultConfig();
        // Register Kubernetes client interface as sigleton
        services.AddSingleton<IKubernetes>(_ => new Kubernetes(kubernetesClientConfig));
        services.Configure<Scaling>(config.GetSection("Scaling"));
        services.AddHttpClient<IPrometheusService,PrometheusService>();
        services.AddSingleton<IKubernetesService, KuberenetesService>();
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
