using System;
using System.Threading.Tasks;

namespace DataCollection
{
	public interface IDataCollection : IDisposable
	{
		void CreateGauge(string gaugeName, string description);
		void SetGauge(string gaugeName, double value);
		
		void CreateCounter(string counterName, string description);
		void IncrementCounter(string counterName, double v);

		// Asynchronously produce a Prometheus-compatible page of metrics		
		Task<byte[]> Generate();
	}
}
