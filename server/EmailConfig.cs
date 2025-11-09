using System;
using System.IO;
using System.Threading.Tasks;

namespace SecureMailingList
{
	public class EmailConfig
	{
		public string PlainTemplate { get; }
		public string HtmlTemplate { get; }
		public string Subject { get; }
		public string FromEmail { get; }
		public string FromName { get; }

		public static async Task<EmailConfig> LoadFromFileAsync(string configPath)
		{
			string[] lines = await File.ReadAllLinesAsync(configPath).ConfigureAwait(false);
			if (lines.Length != 5)
			{
				throw new Exception($"Email config file must have exactly 5 lines:\n1. Plain text template file path\n2. HTML template file path\n3. Email subject\n4. From email address\n5. From display name");
			}
			string plainPath = lines[0].Trim();
			string htmlPath = lines[1].Trim();
			string subject = lines[2].Trim();
			string fromEmail = lines[3].Trim();
			string fromName = lines[4].Trim();
			// Load templates
			string plainTemplate = await File.ReadAllTextAsync(plainPath).ConfigureAwait(false);
			string htmlTemplate = await File.ReadAllTextAsync(htmlPath).ConfigureAwait(false);
			return new EmailConfig(plainTemplate, htmlTemplate, subject, fromEmail, fromName);
		}

		private EmailConfig(string plainTemplate, string htmlTemplate, string subject, string fromEmail, string fromName)
		{
			PlainTemplate = plainTemplate;
			HtmlTemplate = htmlTemplate;
			Subject = subject;
			FromEmail = fromEmail;
			FromName = fromName;
		}
	}
}