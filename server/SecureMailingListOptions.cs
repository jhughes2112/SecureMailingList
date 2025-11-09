using CommandLine;

namespace SecureMailingList
{
	public class SecureMailingListOptions
	{
		//-------------------
		// Connection
		[Option("conn_bindurl", Required = false, Default = "http://+:18888/", HelpText = "Bind address for the web server. /health and /metrics always available.")]
		public string? conn_bindurl { get; set; }

		[Option("hosted_url", Required = true, HelpText = "URL where this server can be reached when a link is clicked. Such as https://example.com/signup")]
		public string? hosted_url { get; set; }

		//-------------------
		// Email template
		[Option("email_cfg", Required = true, HelpText = "Path to email configuration file.")]
		public string? email_cfg { get; set; }

		//-------------------
		// Outputs
		[Option("csvfile", Required = true, HelpText = "Path of the CSV file that holds the email list between reboots.")]
		public string? csvfile { get; set; }

		[Option("download_password", Required = false, Default = "", HelpText = "If set, you can download the current email list directly from the server at /?d=<password>")]
		public string? download_password { get; set; }

		//-------------------
		// Link validity
		[Option("link_valid_seconds", Required = false, Default = 86400, HelpText = "Maximum age (in seconds) a signed link remains valid. 0 disables expiration.")]
		public int link_valid_seconds { get; set; }

		//-------------------
		// SendGrid
		[Option("sendgrid_apikey", Required = true, HelpText = "SendGrid API key for sending emails.")]
		public string? sendgrid_apikey { get; set; }
	}
}
