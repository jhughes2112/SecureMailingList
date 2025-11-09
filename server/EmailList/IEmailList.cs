using System.Collections.Generic;
using System.Threading.Tasks;

namespace SecureMailingList
{
	public class EmailEntry
	{
		public string FullName { get; set; } = string.Empty;
		public List<string> Tags { get; set; } = new List<string>();
	}

	public interface IEmailList
	{
		Task Read(Dictionary<string, EmailEntry> emailList);
		Task Write(Dictionary<string, EmailEntry> emailList);
		Task Test();
	}
}