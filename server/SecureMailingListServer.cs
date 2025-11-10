using DataCollection;
using Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shared;
using Utilities;
using CsvHelper;
using System.Globalization;

namespace SecureMailingList
{
	public class SecureMailingListServer
	{
		private class MutableLong
		{
			public long Value;
		}

		private const string kAddRequestsCounter = "add_requests";
		private const string kEmailSentCounter = "add_success";
		private const string kEmailLinkClicks = "email_link_clicks";
		private const string kRecordUpdateCounter = "record_update";
		private const string kUnsubscribeCounter = "unsubscribes";

		private readonly string _hostedUrl;
		private readonly ILogging _logger;
		private readonly IDataCollection _dataCollection;
		private readonly CancellationTokenSource _cancellationTokenSrc;
		private readonly List<IEmailList> _emailLists;
		private readonly int _linkValidSeconds;
		private readonly IMailSender _mailSender;
		private readonly EmailConfig _emailConfig;

		private readonly Dictionary<string, EmailEntry> _emailList = new Dictionary<string, EmailEntry>();

		private readonly HashSet<string> _allTags = new HashSet<string>();

		// This is a rate limiter by IP address, to prevent mail bombing.
		private readonly ThreadSafeDictionary<string, MutableLong> _ipCounts = new ThreadSafeDictionary<string, MutableLong>();

		private readonly Timer _cleanupTimer;

		private readonly StringSigner _stringSigner;
		private readonly string _publicKey;

		private readonly string _csvFile;
		private readonly string _downloadPassword;

		public SecureMailingListServer(string hostedUrl, EmailConfig emailConfig, List<IEmailList> emailLists, int linkValidSeconds, IMailSender mailSender, IDataCollection dataCollection, ILogging logger, CancellationTokenSource tokenSrc, string csvFile, string downloadPassword)
		{
			_hostedUrl = hostedUrl;
			_emailConfig = emailConfig;
			_emailLists = emailLists;
			_linkValidSeconds = linkValidSeconds;
			_mailSender = mailSender;
			_dataCollection = dataCollection;
			_logger = logger;
			_cancellationTokenSrc = tokenSrc;
			_csvFile = csvFile;
			_downloadPassword = downloadPassword;

			_stringSigner = new StringSigner();
			_publicKey = _stringSigner.GetPublicKey();

			// Start cleanup timer to remove expired IP entries every 60 seconds
			_cleanupTimer = new Timer(CleanupExpiredEntries, null, 60000, 60000);

			// Create metrics
			_dataCollection.CreateCounter(kAddRequestsCounter, "Total add email requests");
			_dataCollection.CreateCounter(kEmailSentCounter, "Emails sent successfully");
			_dataCollection.CreateCounter(kEmailLinkClicks, "Total email link clicks");
			_dataCollection.CreateCounter(kRecordUpdateCounter, "Record updates");
			_dataCollection.CreateCounter(kUnsubscribeCounter, "Unsubscribes");

			_logger.Log(EVerbosity.Info, "Server initialized.");
		}

		public Task Shutdown()
		{
			_logger.Log(EVerbosity.Info, "Server shutting down.");
			_cleanupTimer?.Dispose();
			_logger.Log(EVerbosity.Info, "Server shutdown complete.");
			return Task.CompletedTask;
		}

		public Task<(int, string, byte[])> HandleRequest(HttpListenerContext http)
		{
			http.Response.AddHeader("Access-Control-Allow-Origin", "*");
			string query = Uri.UnescapeDataString(http.Request.Url?.Query ?? "");

			if (query.StartsWith("?r="))  // someone submitted a request to add their email to the list, which we use to send an email to them with a link
			{
				string base64Payload = query.Substring(3);
				string ip = http.Request.RemoteEndPoint?.Address.ToString() ?? "unknown";
				return ProcessRequest(base64Payload, ip);
			}
			else if (query.StartsWith("?v="))  // someone clicked the link in the email to verify their email address
			{
				string base64Payload = query.Substring(3);
				return ProcessVerify(base64Payload);
			}
			else if (query.StartsWith("?d="))
			{
				string pass = query.Substring(3);
				if (!string.IsNullOrEmpty(_downloadPassword) && pass == _downloadPassword)
				{
					if (File.Exists(_csvFile))
					{
						_logger.Log(EVerbosity.Info, "Download successful");
						http.Response.AddHeader("Content-Disposition", "attachment; filename=\"emaillist.csv\"");
						byte[] content = File.ReadAllBytes(_csvFile);
						return Task.FromResult((200, "text/csv", content));
					}
					else
					{
						return Task.FromResult((404, "text/plain", Encoding.UTF8.GetBytes("File not found")));
					}
				}
				else
				{
					_logger.Log(EVerbosity.Info, "Download failed: bad password");
					return Task.FromResult((404, "text/plain", Encoding.UTF8.GetBytes("Not Found")));
				}
			}
			else
			{
				return Task.FromResult((404, "text/plain", Encoding.UTF8.GetBytes("Not Found")));
			}
		}

		private async Task<(int, string, byte[])> ProcessRequest(string base64Payload, string ip)
		{
			_dataCollection.IncrementCounter(kAddRequestsCounter, 1);

			try
			{
				byte[] payloadBytes = UrlHelper.Base64UrlDecodeBytes(base64Payload);
				string payload = Encoding.UTF8.GetString(payloadBytes);
				string[] parts;
				using (CsvParser parser = new CsvParser(new StringReader(payload), CultureInfo.InvariantCulture))
				{
					if (!parser.Read())
					{
						return (400, "text/plain", Encoding.UTF8.GetBytes("Invalid request"));
					}
					parts = parser.Record ?? Array.Empty<string>();
				}

				// Validate request parameters
				if (parts.Length < 2 || !RegexHelper.Email.IsMatch(parts[0]))
				{
					return (400, "text/plain", Encoding.UTF8.GetBytes("Invalid request"));
				}

				string email = parts[0];

				// Check if IP has recent request
				long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				if (_ipCounts.TryGetValue(ip, out MutableLong? val) && val.Value > now)
				{
					long remaining = val.Value - now;
					_logger.Log(EVerbosity.Info, $"Too many requests for email: {email}, remaining {remaining} seconds");
					return (429, "text/plain", Encoding.UTF8.GetBytes($"Too many requests. Try again in {remaining} seconds."));
				}
				else
				{
					_ipCounts.AddOrUpdate(ip, new MutableLong { Value = now + 60 });
				}

				string fullname = parts[1];
				List<string> tags = new List<string>();
				for (int i = 2; i < parts.Length; i++)
				{
					string tag = parts[i];
					if (!string.IsNullOrEmpty(tag))
					{
						tags.Add(tag);
						_allTags.Add(tag);
					}
				}

				long timestamp = _linkValidSeconds == 0 ? 0 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _linkValidSeconds;
				string newPayload = $"\"{email}\",\"{fullname}\",\"{timestamp}\"";
				foreach (string tag in tags)
				{
					newPayload += $",\"{tag}\"";
				}
				string unsignedBase64 = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(newPayload));
				string signedPayload = _stringSigner.Sign(unsignedBase64);
				string link = $"{_hostedUrl}?v={signedPayload}";

				// Send email
				string htmlBody = _emailConfig.HtmlTemplate.Replace("{{LINK}}", link).Replace("{{FROMNAME}}", _emailConfig.FromName).Replace("{{FROMEMAIL}}", _emailConfig.FromEmail).Replace("{{USERNAME}}", fullname);
				string plainBody = _emailConfig.PlainTemplate.Replace("{{LINK}}", link).Replace("{{FROMNAME}}", _emailConfig.FromName).Replace("{{FROMEMAIL}}", _emailConfig.FromEmail).Replace("{{USERNAME}}", fullname);
				int status = await _mailSender.Send(email, fullname, _emailConfig.FromEmail, _emailConfig.FromName, _emailConfig.Subject, plainBody, htmlBody).ConfigureAwait(false);

				if (status >= 200 && status < 300)  // SendGrid uses 202 as a successful send
				{
					_logger.Log(EVerbosity.Info, $"Email sent to {email}, {fullname}, categories: {string.Join(",", tags)}");
					_dataCollection.IncrementCounter(kEmailSentCounter, 1);
					return (status, "text/plain", Encoding.UTF8.GetBytes("Email sent"));
				}
				else
				{
					return (status, "text/plain", Encoding.UTF8.GetBytes("Failed to send email"));
				}
			}
			catch
			{
				return (500, "text/plain", Encoding.UTF8.GetBytes("Error"));
			}
		}

		private async Task<(int, string, byte[])> ProcessVerify(string base64Payload)
		{
			_dataCollection.IncrementCounter(kEmailLinkClicks, 1);

			// Verify signature
			if (!StringSigner.Verify(base64Payload, _publicKey))
			{
				_logger.Log(EVerbosity.Info, "Invalid signature");
				return (400, "text/plain", Encoding.UTF8.GetBytes("Invalid signature"));
			}

			// Extract the actual payload
			string[] signedParts = base64Payload.Split('.');
			string actualBase64Payload = signedParts[0];

			try
			{
				byte[] payloadBytes = UrlHelper.Base64UrlDecodeBytes(actualBase64Payload);
				string payload = Encoding.UTF8.GetString(payloadBytes);
				string[] parts;
				using (CsvParser parser = new CsvParser(new StringReader(payload), CultureInfo.InvariantCulture))
				{
					if (!parser.Read())
					{
						return (400, "text/plain", Encoding.UTF8.GetBytes("Invalid data"));
					}
					parts = parser.Record ?? Array.Empty<string>();
				}

				if (parts.Length < 3)
				{
					_logger.Log(EVerbosity.Info, "Invalid data");
					return (400, "text/plain", Encoding.UTF8.GetBytes("Invalid data"));
				}

				string email = parts[0];
				string fullname = parts[1];
				long timestamp = long.Parse(parts[2]);
				List<string> tags = new List<string>();
				for (int i = 3; i < parts.Length; i++)
				{
					string tag = parts[i];
					if (!string.IsNullOrEmpty(tag))
					{
						tags.Add(tag);
						_allTags.Add(tag);
					}
				}

				long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				if (timestamp != 0 && now > timestamp)
				{
					_logger.Log(EVerbosity.Info, $"Link expired for {email}");
					return (400, "text/plain", Encoding.UTF8.GetBytes("Link expired"));
				}

				// Update list
				string responseText = string.Empty;
				if (tags.Count == 0)
				{
					_emailList.Remove(email);
					_dataCollection.IncrementCounter(kUnsubscribeCounter, 1);
					responseText = $"Confirmed unsubscribed {email}";
				}
				else
				{
					_emailList[email] = new EmailEntry { FullName = fullname, Tags = tags };
					_dataCollection.IncrementCounter(kRecordUpdateCounter, 1);
					responseText = $"Confirmed subscribed to {email} to: {string.Join(',', tags)}";
				}

				_logger.Log(EVerbosity.Info, $"VERIFIED: {email}, {fullname}, categories: {string.Join(",", tags)}");

				// Save email list
				await SaveEmailListAsync().ConfigureAwait(false);

				return (200, "text/plain", Encoding.UTF8.GetBytes(responseText));
			}
			catch
			{
				return (500, "text/plain", Encoding.UTF8.GetBytes("Error"));
			}
		}

		private async Task SaveEmailListAsync()
		{
			foreach (IEmailList list in _emailLists)
			{
				await list.Write(_emailList).ConfigureAwait(false);
			}
		}

		public async Task LoadEmailListAsync()
		{
			foreach (IEmailList list in _emailLists)
			{
				await list.Read(_emailList).ConfigureAwait(false);
			}
		}

		private void CleanupExpiredEntries(object? state)
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			List<string> toRemove = new List<string>();
			_ipCounts.Foreach((ip, val) =>
			{
				if (val.Value <= now)
				{
					toRemove.Add(ip);
				}
			});
			foreach (string ip in toRemove)
			{
				_ipCounts.TryRemove(ip, out _);
			}
			if (toRemove.Count>0)
			{
				_logger.Log(EVerbosity.Info, $"Cleaned up {toRemove.Count} expired IP entries");
			}
		}

		public async Task Test()
		{
			// Test ProcessRequest valid
			string payload = $"{_emailConfig.FromEmail},{_emailConfig.FromName},tag1,tag2";
			string base64Payload = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(payload));
			var result1 = await ProcessRequest(base64Payload, "127.0.0.1").ConfigureAwait(false);
			if (result1.Item1 < 200 || result1.Item1 >= 300)
			{
				throw new Exception($"Test Request Valid failed: {result1.Item1} {Encoding.UTF8.GetString(result1.Item3)}");
			}

			// Test ProcessRequest rate limit (second request from same IP should fail)
			var result1RateLimit = await ProcessRequest(base64Payload, "127.0.0.1").ConfigureAwait(false);
			if (result1RateLimit.Item1 != 429)
			{
				throw new Exception($"Test Request Rate Limited failed: {result1RateLimit.Item1} {Encoding.UTF8.GetString(result1RateLimit.Item3)}");
			}

			// Test ProcessRequest from different IP (should succeed)
			var result1DifferentIP = await ProcessRequest(base64Payload, "127.0.0.2").ConfigureAwait(false);
			if (result1DifferentIP.Item1 < 200 || result1DifferentIP.Item1 >= 300)
			{
				throw new Exception($"Test Request Different IP failed: {result1DifferentIP.Item1} {Encoding.UTF8.GetString(result1DifferentIP.Item3)}");
			}

			// Test ProcessRequest invalid email
			string invalidPayload = "invalid,John,tag1";
			string invalidBase64 = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(invalidPayload));
			var result1b = await ProcessRequest(invalidBase64, "127.0.0.3").ConfigureAwait(false);
			if (result1b.Item1 != 400)
			{
				throw new Exception($"Test Request Invalid Email failed: {result1b.Item1} {Encoding.UTF8.GetString(result1b.Item3)}");
			}

			// Test ProcessRequest too few parts
			string shortPayload = "email";
			string shortBase64 = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(shortPayload));
			var result1c = await ProcessRequest(shortBase64, "127.0.0.4").ConfigureAwait(false);
			if (result1c.Item1 != 400)
			{
				throw new Exception($"Test Request Too Few Parts failed: {result1c.Item1} {Encoding.UTF8.GetString(result1c.Item3)}");
			}

			// Test ProcessRequest exception (invalid email format)
			string badPayload = $"notanemail,{_emailConfig.FromName},notemail,tag1";
			string badBase64 = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(badPayload));
			var result1d = await ProcessRequest(badBase64, "127.0.0.5").ConfigureAwait(false);
			if (result1d.Item1 != 400)
			{
				throw new Exception($"Test Request Exception failed: {result1d.Item1} {Encoding.UTF8.GetString(result1d.Item3)}");
			}

			// Test ProcessVerify valid
			string verifyPayload = $"{_emailConfig.FromEmail},{_emailConfig.FromName},0,tag1,tag2";
			string verifyBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(verifyPayload));
			string verifyBase64 = _stringSigner.Sign(verifyBase64Unsigned);
			var result2 = await ProcessVerify(verifyBase64).ConfigureAwait(false);
			if (result2.Item1 != 200)
			{
				throw new Exception($"Test Verify Valid failed: {result2.Item1} {Encoding.UTF8.GetString(result2.Item3)}");
			}

			// Test ProcessVerify expired
			string expiredPayload = $"{_emailConfig.FromEmail},{_emailConfig.FromName}," + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100) + ",tag1";
			string expiredBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(expiredPayload));
			string expiredBase64 = _stringSigner.Sign(expiredBase64Unsigned);
			var result2b = await ProcessVerify(expiredBase64).ConfigureAwait(false);
			if (result2b.Item1 != 400)
			{
				throw new Exception($"Test Verify Expired failed: {result2b.Item1} {Encoding.UTF8.GetString(result2b.Item3)}");
			}

			// Test ProcessVerify not expired (timestamp 0)
			string noExpirePayload = $"{_emailConfig.FromEmail},{_emailConfig.FromName},0,tag1";
			string noExpireBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(noExpirePayload));
			string noExpireBase64 = _stringSigner.Sign(noExpireBase64Unsigned);
			var result2c = await ProcessVerify(noExpireBase64).ConfigureAwait(false);
			if (result2c.Item1 != 200)
			{
				throw new Exception($"Test Verify No Expire failed: {result2c.Item1} {Encoding.UTF8.GetString(result2c.Item3)}");
			}

			// Test ProcessVerify unsubscribe
			string unsubPayload = $"{_emailConfig.FromEmail},{_emailConfig.FromName},0";
			string unsubBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(unsubPayload));
			string unsubBase64 = _stringSigner.Sign(unsubBase64Unsigned);
			var result3 = await ProcessVerify(unsubBase64).ConfigureAwait(false);
			if (result3.Item1 != 200)
			{
				throw new Exception($"Test Unsubscribe failed: {result3.Item1} {Encoding.UTF8.GetString(result3.Item3)}");
			}

			// Test ProcessVerify invalid data
			string invalidDataPayload = "email";
			string invalidDataBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(invalidDataPayload));
			string invalidDataBase64 = _stringSigner.Sign(invalidDataBase64Unsigned);
			var result3b = await ProcessVerify(invalidDataBase64).ConfigureAwait(false);
			if (result3b.Item1 != 400)
			{
				throw new Exception($"Test Verify Invalid Data failed: {result3b.Item1} {Encoding.UTF8.GetString(result3b.Item3)}");
			}

			// Test ProcessVerify exception
			string badVerifyPayload = $"{_emailConfig.FromEmail},{_emailConfig.FromName},notnumber";
			string badVerifyBase64Unsigned = UrlHelper.Base64UrlEncodeNoPadding(Encoding.UTF8.GetBytes(badVerifyPayload));
			string badVerifyBase64 = _stringSigner.Sign(badVerifyBase64Unsigned);
			var result3c = await ProcessVerify(badVerifyBase64).ConfigureAwait(false);
			if (result3c.Item1 != 500)
			{
				throw new Exception($"Test Verify Exception failed: {result3c.Item1} {Encoding.UTF8.GetString(result3c.Item3)}");
			}

			// Test email lists
			foreach (IEmailList list in _emailLists)
			{
				await list.Test().ConfigureAwait(false);
			}
		}
	}
}
