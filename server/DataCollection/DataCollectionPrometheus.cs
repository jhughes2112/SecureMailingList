using Logging;
using Prometheus;
using Prometheus.DotNetRuntime;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Utilities;

namespace DataCollection
{
	// Implements the metrics interface for Prometheus scraping.
    public class DataCollectionPrometheus : IDataCollection
    {
		private class ValueDesc
		{
			public ValueDesc(long value, string description)
			{
				this.value = value;
				this.description = description;
			}
			public long   value;
			public string description;
		}

		private ThreadSafeDictionary<string, Gauge>   _gauges        = new ThreadSafeDictionary<string, Gauge>();
		private ThreadSafeDictionary<string, Counter> _counters      = new ThreadSafeDictionary<string, Counter>();
		private IDisposable?                          _collector     = null;

		// Labels automatically tag all gauges and counters that are created so they can be easily queried, such as program->cluster or zone->tidesreach
		public DataCollectionPrometheus(Dictionary<string, string> labels, ILogging logger)
		{
			Metrics.DefaultRegistry.SetStaticLabels(labels);
			_collector = DotNetRuntimeStatsBuilder.Customize()
				.WithContentionStats()
				.WithExceptionStats()
				.WithGcStats()
				.WithJitStats()
				.WithThreadPoolStats()
				.WithErrorHandler(err => logger.Log(EVerbosity.Error, $"Prometheus: {err}"))
				.WithSocketStats()
//				.RecycleCollectorsEvery(TimeSpan.FromDays(1))
				.StartCollecting();
		}

		public void Dispose()
		{
			_collector?.Dispose();  // This stops the collector too.
			_collector = null;
		}

		public void CreateGauge(string gaugeName, string description)
		{
			if (RegexHelper.PrometheusName.IsMatch(gaugeName)==false)
				throw new Exception($"DataCollection.CreateGauge Invalid name format (only letters, numbers, and underscores): {gaugeName}");
			if (_gauges.Add(gaugeName, Metrics.CreateGauge(gaugeName, description))==false)
				throw new Exception($"DataCollectionPrometheus.CreateGauge Failed to add {gaugeName}.  Already exists?");
		}

		public void CreateCounter(string counterName, string description)
		{
			if (RegexHelper.PrometheusName.IsMatch(counterName)==false)
				throw new Exception($"DataCollection.CreateGauge Invalid name format (only letters, numbers, and underscores): {counterName}");
			_counters.GetOrAdd(counterName, () => Metrics.CreateCounter(counterName, description));
		}

		public void IncrementCounter(string counterName, double v)
		{
			if (_counters.TryGetValue(counterName, out Counter c)==false)
				throw new Exception($"DataCollection.IncrementCounter missing {counterName}");
			c.Inc(v);
		}

		public void SetGauge(string gaugeName, double value)
		{
			if (_gauges.TryGetValue(gaugeName, out Gauge g)==false)
				throw new Exception($"DataCollection.SetGauge missing {gaugeName}");
			g.Set(value);
		}

		public async Task<byte[]> Generate()
		{
			using (MemoryStream ms = new MemoryStream())
			{
				await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(ms).ConfigureAwait(false);
				return ms.ToArray();
			}
		}
    }
}
