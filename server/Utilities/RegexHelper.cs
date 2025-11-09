using System.Text.RegularExpressions;

namespace Utilities
{
	static public class RegexHelper
	{
		static public Regex PrometheusName = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

		static public Regex Email = new Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
	}
}