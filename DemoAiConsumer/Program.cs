using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace DemoAiConsumer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(AppContext.BaseDirectory);
                    configHost.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    configHost.AddEnvironmentVariables(prefix: "DOTNET_");
                    configHost.AddCommandLine(args);
                    configHost.AddInMemoryCollection([new KeyValuePair<string, string?>("Environment", "Development")]!);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<BackgroundService>();
                    services.AddHttpClient();
                })
                .Build();

            await host.RunAsync();
        }
    }

    public class BackgroundService(ILogger<BackgroundService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        : Microsoft.Extensions.Hosting.BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var apiKey = configuration["AiSettings:AuthKey"];

            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("AI Auth Key not found in configuration.");
                return;
            }
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var client = httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var response = await client.GetAsync("https://api.venice.ai/api/v1/models", stoppingToken);

                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(stoppingToken);
                    logger.LogInformation("Available Models: {Content}", content);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error calling VeniceAI API.");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}