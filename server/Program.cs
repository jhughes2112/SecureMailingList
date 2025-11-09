using ReachableGames;
using CommandLine;
using DataCollection;
using Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using Networking;

namespace SecureMailingList
{
	public class Program
	{
		static public async Task Main(string[] args)
		{
			await Parser.Default.ParseArguments<SecureMailingListOptions>(args).WithParsedAsync(Run).ConfigureAwait(false);
		}

		static private async Task Run(SecureMailingListOptions o)
		{
			CancellationTokenSource tokenSrc = new CancellationTokenSource();

			// Set up a callback so a ^C will halt the server
			bool sigIntRecvd = false;
			Console.CancelKeyPress += new ConsoleCancelEventHandler((object? sender, ConsoleCancelEventArgs e) =>
				{
					Console.WriteLine("Caught SIGINT, tripping cancellation token.");   // Control-C
					e.Cancel = true;
					sigIntRecvd = true;
					tokenSrc.Cancel();
				});

			// Set up a callback to have SIGTERM also halt the server gracefully.  This is what "docker stop" uses.
			AssemblyLoadContext? ctx = AssemblyLoadContext.GetLoadContext(typeof(Program).GetTypeInfo().Assembly);
			if (ctx!=null)
			{
				ctx.Unloading += (AssemblyLoadContext context) =>
				{
					if (sigIntRecvd==false)  // don't process this if control-c happened 
					{
						Console.WriteLine("Caught SIGTERM, tripping cancellation token.");  // SIGTERM / kill
						tokenSrc.Cancel();
					}
				};
			}

			// Move resource definitions outside try/catch so they can be properly disposed in finally
			ILogging? logger = null;
			IDataCollection? dataCollection = null;
			SecureMailingListServer? server = null;
			ReachableGames.RGWebSocket.WebServer? webServer = null;
			
			try
			{
				// Simple logger
				logger = new LoggingConsole("SecureMailingList", EVerbosity.Info);
				// Simple data collection
				dataCollection = new DataCollectionPrometheus(new Dictionary<string, string>() { { "process", "SecureMailingList" } }, logger);
				Constants.Initialize(dataCollection);

				// Load email config
				EmailConfig emailConfig = await EmailConfig.LoadFromFileAsync(o.email_cfg!).ConfigureAwait(false);

				// Create email lists
				List<IEmailList> emailLists = new List<IEmailList>();
				emailLists.Add(new EmailListCSV(o.csvfile!));

				// Create mail sender
				IMailSender mailSender = new MailSenderSendGrid(o.sendgrid_apikey!);

				// The reason this takes in a CancellationTokenSource is Docker/someone may hit ^C and want to shutdown the server.
				// The reason we explicitly call Shutdown is the server itself may exit for other reasons, and we need to make sure it shuts down in either case.
				server = new SecureMailingListServer(o.hosted_url!, emailConfig, emailLists, o.link_valid_seconds, mailSender, dataCollection, logger, tokenSrc, o.csvfile!, o.download_password ?? "");

				// Load email list
				await server.LoadEmailListAsync().ConfigureAwait(false);

				// Set up a websocket handler that forwards connections, disconnections, and messages to the ClusterServer
				ConnectionManagerReject connectionMgr = new ConnectionManagerReject(logger);
				webServer = new ReachableGames.RGWebSocket.WebServer(o.conn_bindurl!, 20, 1000, 5, connectionMgr, logger);

				// (responseCode, responseContentType, responseContent)
				webServer.RegisterExactEndpoint("/metrics", async (HttpListenerContext context) => { return (200, "text/plain", await dataCollection.Generate()); });
				webServer.RegisterExactEndpoint("/health", (HttpListenerContext) => { return Task.FromResult((200, "text/plain", new byte[0])); } );

				// Explicit API handlers
				webServer.RegisterPrefixEndpoint("/", server.HandleRequest);

				webServer.Start();  // this starts the webserver in a separate thread

				// Do some basic tests.
//				await server.Test().ConfigureAwait(false);

				await tokenSrc.Token;  // block here until the cancellation token triggers.  Note, if the server decides to shut itself down, IT CANCELS THIS TOKEN.  So this is the perfect way to wait.
			}
			catch (OperationCanceledException)
			{
				// flow control
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
			}
			finally
			{
				webServer?.UnregisterPrefixEndpoint("/");
				webServer?.UnregisterExactEndpoint("/metrics");
				webServer?.UnregisterExactEndpoint("/health");

				if (server != null)
				{
					await server.Shutdown().ConfigureAwait(false);
				}
				
				// Dispose of resources that implement IDisposable
				dataCollection?.Dispose();
				logger?.Dispose();
				// authentication does not implement IDisposable, so no disposal needed
			}
		}
    }
}