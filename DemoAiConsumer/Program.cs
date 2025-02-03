using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                    configHost.AddInMemoryCollection([
                        new KeyValuePair<string, string?>("Environment", "Development")
                    ]!);
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

    public class BackgroundService(
        ILogger<BackgroundService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
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

                    //await ProcessModelList(client, stoppingToken);
                    await ProcessImage(client, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error calling VeniceAI API.");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task ProcessModelList(HttpClient client, CancellationToken stoppingToken)
        {
            var response = await client.GetAsync("https://api.venice.ai/api/v1/models", stoppingToken);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(stoppingToken);
            logger.LogInformation("Available Models: {Content}", content);
        }

        private async Task ProcessImage(HttpClient client, CancellationToken stoppingToken)
        {
            var request = new GenerateImageRequest
            {
                Model = "stable-diffusion-3.5",
                Prompt = "Draw an image of the inside of a huge old library with a person standing in the middle looking out at all the books. He is standing in owe of the vast old library. There is a spacious feel, and a feeling of loads of ancient wisdom being present. Choose randomly between watercolour, oil paint, cartoon, pencil sketch style images.",
                ReturnBinary = true
            };

            var jsonBody = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response =
                await client.PostAsync("https://api.venice.ai/api/v1/image/generate", content, stoppingToken);
            response.EnsureSuccessStatusCode();

            var imageData = await response.Content.ReadAsByteArrayAsync(stoppingToken);

            var imagePath = Path.Combine(Environment.CurrentDirectory, "Images", $"generated_image_{DateTime.Now.Ticks}.png");
            await SaveImageToDiskAsync(imageData, imagePath, stoppingToken);
            logger.LogInformation("Image saved to {ImagePath}", imagePath);
        }

        private async Task SaveImageToDiskAsync(byte[] imageData, string filePath, CancellationToken cancellationToken)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving image to disk.");
            }
        }
    }


    public class GenerateImageRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; }

        [JsonPropertyName("prompt")] public string Prompt { get; set; }

        [JsonPropertyName("return_binary")] public bool ReturnBinary { get; set; }
    }

    public class GenerateImageResponse
    {
        public bool Success { get; set; }
        public byte[] ImageData { get; set; }
        public string ErrorMessage { get; set; }
    }
}