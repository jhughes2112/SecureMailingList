using System.Threading.Tasks;

namespace SecureMailingList
{
	public interface IMailSender
	{
		Task<int> Send(string _to, string _toName, string _from, string _fromName, string _subject, string _content, string _htmlContent);
	}
}