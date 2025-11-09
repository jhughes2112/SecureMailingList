using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace SecureMailingList
{
	public class MailSenderSendGrid : IMailSender
	{
		private readonly string _apiKey;

		public MailSenderSendGrid(string apiKey)
		{
			_apiKey = apiKey;
		}

		public async Task<int> Send(string _to, string _toName, string _from, string _fromName, string _subject, string _content, string _htmlContent)
		{
            var client = new SendGridClient(_apiKey);
            var from = new EmailAddress(_from, _fromName);
            var to = new EmailAddress(_to, _toName);
            var msg = MailHelper.CreateSingleEmail(from, to, _subject, _content, _htmlContent);
            var response = await client.SendEmailAsync(msg).ConfigureAwait(false);
			return (int)response.StatusCode;
		}
	}
}
