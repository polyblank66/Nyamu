using System;
using UnityEngine;

namespace Nyamu
{
	/// <summary>
	/// Custom logger to make it easy disable different log types
	///
	/// Implementation Details:
	/// Any class whose name ends with "Logger" that implements a method starting with "Log" is ignored
	/// by the console's double click, unless it is the last call in the stack trace.
	/// https://www.reddit.com/r/Unity3D/comments/17eikh0/i_found_a_way_to_go_to_the_right_line_in_your/
	/// </summary>
	public static class NyamuLogger
	{
		public enum LogLevel
		{
			All,
			Debug,
			Info,
			Warning,
			Error,
			Exception,
			None
		}

		[HideInCallstack]
		public static void LogDebug(string message)
		{
			LogInternal(message, LogLevel.Debug);
		}

		[HideInCallstack]
		public static void LogInfo(string message)
		{
			LogInternal(message, LogLevel.Info);
		}

		[HideInCallstack]
		public static void LogWarning(string message)
		{
			LogInternal(message, LogLevel.Warning);
		}

		[HideInCallstack]
		public static void LogError(string message)
		{
			LogInternal(message, LogLevel.Error);
		}

		[HideInCallstack]
		private static void LogInternal(string message, LogLevel logLevel)
		{
			try
			{
				if (logLevel >= NyamuSettings.Instance.minLogLevel)
					Debug.unityLogger.Log(Map(logLevel), message);
			}
			catch (Exception e)
			{
				Debug.unityLogger.Log(Map(logLevel), message);
				Debug.LogException(e);
			}
		}

		private static LogType Map(LogLevel minLogLevel)
		{
			switch (minLogLevel)
			{
				case LogLevel.None:
				case LogLevel.All:
				case LogLevel.Debug:
				case LogLevel.Info:
				default:
					return LogType.Log;
				case LogLevel.Warning:
					return LogType.Warning;
				case LogLevel.Error:
					return LogType.Error;
				case LogLevel.Exception:
					return LogType.Exception;
			}
		}
	}
}