namespace DataCollection
{
	static public class Constants
	{
		public const string kCounter_MessagesReceived   = "conn_msgs_recv";
		public const string kCounter_MessagesSent       = "conn_msgs_sent";
		public const string kGauge_Concurrents          = "conn_concurrents";

		static public void Initialize(IDataCollection dc)
		{
			dc.CreateCounter(kCounter_MessagesReceived, "Messages received");
			dc.CreateCounter(kCounter_MessagesSent, "Messages sent");
			dc.CreateGauge(kGauge_Concurrents, "Concurrent connections");
		}
	}
}