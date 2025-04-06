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
            string prompt = @"Draw a 2d square board of checked blue and red squares.";

            var request = new GenerateImageRequest
            {
                Model = "stable-diffusion-3.5-rev2",
                Prompt = prompt,
                Width = 1024,
                Height = 1024,
                SafeMode = true,
                HideWatermark = false,
                StylePreset = "Photographic",
                NegativePrompt = "human",
                ReturnBinary = true,
                Format = "webp",
                CfgScale = 2.0f
            };

            try
            {
                var jsonBody = JsonSerializer.Serialize(request);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.venice.ai/api/v1/image/generate", content, stoppingToken);
                response.EnsureSuccessStatusCode();

                var imageData = await response.Content.ReadAsByteArrayAsync(stoppingToken);

                var imagePath = Path.Combine(Environment.CurrentDirectory, "Images",
                    $"generated_image_{DateTime.Now.Ticks}.png");
                await SaveImageToDiskAsync(imageData, imagePath, stoppingToken);
                logger.LogInformation("Image saved to {ImagePath}", imagePath);
            }
            catch (HttpRequestException ex) when (ex.InnerException is TaskCanceledException)
            {
                logger.LogWarning("Request timed out. Retrying...");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Request Error: {ex.Message}");                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
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

        [JsonPropertyName("width")] public int Width { get; set; }

        [JsonPropertyName("height")] public int Height { get; set; }

        [JsonPropertyName("safe_mode")] public bool SafeMode { get; set; }

        [JsonPropertyName("hide_watermark")] public bool HideWatermark { get; set; }

        [JsonPropertyName("cfg_scale")] public double CfgScale { get; set; }

        [JsonPropertyName("style_preset")] public string StylePreset { get; set; }

        [JsonPropertyName("negative_prompt")] public string NegativePrompt { get; set; }

        [JsonPropertyName("format")] public string Format { get; set; }

        [JsonPropertyName("return_binary")] public bool ReturnBinary { get; set; }
    }

    public class GenerateImageResponse
    {
        public bool Success { get; set; }
        public byte[] ImageData { get; set; }
        public string ErrorMessage { get; set; }
    }
}