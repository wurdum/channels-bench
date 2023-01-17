using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<BenchmarkRunner>();

builder.Services.AddOpenTelemetry()
    .WithMetrics(b =>
    {
        b.AddPrometheusExporter()
            .AddRuntimeInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddMeter(BenchmarkRunner.Namespace);
    });

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapGet("/", () => Environment.ProcessId);
app.Run();

public class BenchmarkRunner : BackgroundService
{
    public const string Namespace = nameof(BenchmarkRunner);
    private static readonly Meter Meter = new(Namespace);
    private static readonly Counter<long> MessagesDropped = Meter.CreateCounter<long>("messages_dropped", "total");
    private static readonly Histogram<long> ProcessingLatency = Meter.CreateHistogram<long>("processing_latency", "ms");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await RunBenchmarkAsync(stoppingToken);
        }
        catch (OperationCanceledException oce) when (oce.CancellationToken == stoppingToken)
        {
            // Benchmark stopped.
        }
    }

    private static async Task RunBenchmarkAsync(CancellationToken stoppingToken)
    {
        var channels = new List<Channel<Message>>();
        for (var i = 0; i < 4; i++)
        {
            channels.Add(Channel.CreateBounded<Message>(
                new(10000)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest
                },
                _ => MessagesDropped.Add(1)));
        }

        var consumers = new List<Task>();
        for (var index = 0; index < channels.Count; index++)
        {
            var channelIndex = index;
            var channel = channels[index];

            consumers.Add(Task.Run(async () =>
            {
                await foreach (var message in channel.Reader.ReadAllAsync(stoppingToken))
                {
                    ProcessingLatency.Record((long)Math.Abs((DateTime.UtcNow - message.Timestamp).TotalMilliseconds), new TagList
                    {
                        { "consumer", channelIndex }
                    });
                }
            }, stoppingToken));
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var message = new Message(DateTime.UtcNow);
            foreach (var channel in channels)
            {
                await channel.Writer.WriteAsync(message, stoppingToken);

                await Task.Delay(1, stoppingToken);
            }
        }

        await Task.WhenAll(consumers);
    }
}

internal record Message(DateTime Timestamp);
