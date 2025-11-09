using System.Threading.Tasks;
using ReachableGames.RGWebSocket;
using System.Net;
using Logging;

namespace Networking
{
	// Whenever there's a new connection or a disconnection or indeed ANY message received, they all come through this object regardless of what websocket originates it.
	// ConnectionManagerReject, as a policy, rejects all connections.
	public class ConnectionManagerReject : IConnectionManager
	{
		private ILogging          _logger;

		public ConnectionManagerReject(ILogging logger)
		{
			_logger            = logger;
		}

		public async Task OnConnection(RGWebSocket rgws, HttpListenerContext httpListenerContext)
		{
			string displayName = rgws._displayId;
			_logger.Log(EVerbosity.Info, $"OnConnection called\" RGWSID={displayName}");
			await rgws.Shutdown().ConfigureAwait(false);
		}

		public Task OnDisconnect(RGWebSocket rgws)
		{
			return Task.CompletedTask;
		}

		public Task OnReceiveBinary(RGWebSocket rgws, PooledArray pa) 
		{ 
			return Task.CompletedTask;  // also ignore binary messages
		}
		public Task OnReceiveText(RGWebSocket rgws, string msg)
		{
			return Task.CompletedTask;  // strictly ignore text messages from the websocket connections
		}

		public Task Shutdown()
		{
			return Task.CompletedTask;
		}
	}
}
