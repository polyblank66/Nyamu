namespace Nyamu
{
	public static class NyamuLog
	{
		public enum MinLogLevel
		{
			All,
			Debug,
			Info,
			Warning,
			Error,
			Exception,
			None
		}

		public static void Debug(string message)
		{
			if (NyamuSettings.Instance.minLogLevel >= MinLogLevel.Debug)
				UnityEngine.Debug.Log(message);
		}

		public static void Info(string message)
		{
			if (NyamuSettings.Instance.minLogLevel >= MinLogLevel.Info)
				UnityEngine.Debug.Log(message);
		}

		public static void Warning(string message)
		{
			if (NyamuSettings.Instance.minLogLevel >= MinLogLevel.Warning)
				UnityEngine.Debug.Log(message);
		}

		public static void Error(string message)
		{
			if (NyamuSettings.Instance.minLogLevel >= MinLogLevel.Error)
				UnityEngine.Debug.Log(message);
		}
	}
}