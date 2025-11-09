using Nito.AsyncEx;
using ReachableGames.RGWebSocket;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Logging
{
    public class LoggingConsole : ILogging
    {
        public EVerbosity Verbosity { get; set; }
		
		private string                   _prefix;
		private Task?                    _asyncLogTask       = Task.CompletedTask;
		private long                     _startTime          = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		private LockingList<string>      _logs               = new LockingList<string>();
		private AsyncAutoResetEvent      _logsAvailable      = new AsyncAutoResetEvent(false);
		private CancellationTokenSource? _cancellationSource = new CancellationTokenSource();

        public LoggingConsole(string prefix, EVerbosity verbosity)
        {
            Verbosity = verbosity;
			_prefix   = prefix;
			_asyncLogTask = Task.Run(async () => await AsyncLogTask(_cancellationSource.Token).ConfigureAwait(false));
        }

		public void Dispose()
		{
			_cancellationSource?.Cancel();
			_asyncLogTask?.GetAwaiter().GetResult();  // block until the thread exits
			_asyncLogTask = null;
			_cancellationSource?.Dispose();
			_cancellationSource = null;
		}

        public void Log(EVerbosity level, string msg)
        {
            if (Verbosity >= level)
			{
				long msFromStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTime;
				_logs.Add($"{TimeSpan.FromMilliseconds(msFromStart).ToString(@"hh\:mm\:ss\:fff")} V={(int)level} {_prefix} {msg}");
				_logsAvailable.Set();
			}
        }

		private async Task AsyncLogTask(CancellationToken token)
		{
			try
			{
				List<string> logsToWrite = new List<string>();
				while (token.IsCancellationRequested==false)
				{
					try
					{
						await _logsAvailable.WaitAsync(token).ConfigureAwait(false);  // new log showed up, send it to the file
						_logs.MoveTo(logsToWrite);
						foreach (string l in logsToWrite)
						{
							Console.WriteLine(l);
						}
						logsToWrite.Clear();
					}
					catch (OperationCanceledException)
					{
						// flow control
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}
    }
}