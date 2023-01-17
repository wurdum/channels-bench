# .NET Channels Benchmark

This is a benchmark of the throughput of .NET Channels implementation.

To run the benchmark start Prometheus container to collect metrics:

```bash
cd ./prometheus
docker-compose up -d
```

Then run the application:

```bash
dotnet run -c Release --urls "http://+:5001" --project src/ChannelsBench.csproj
```

Open the following URL in your browser to see the results:

http://localhost:9090/graph?g0.expr=histogram_quantile(0.99%2C%20sum(rate(processing_latency_ms_bucket%5B30s%5D))%20by%20(le%2C%20consumer))&g0.tab=0&g0.stacked=0&g0.show_exemplars=0&g0.range_input=1h&g1.expr=rate(messages_dropped_total%5B30s%5D)&g1.tab=0&g1.stacked=0&g1.show_exemplars=0&g1.range_input=1h
