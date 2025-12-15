using System;
using System.Diagnostics;
using UnityEngine;

namespace Nyamu
{
	public static class NyamuLog
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

		public static void Debug(string message)
		{
			LogInternal(message, LogLevel.Debug);
		}

		public static void Info(string message)
		{
			LogInternal(message, LogLevel.Info);
		}

		public static void Warning(string message)
		{
			LogInternal(message, LogLevel.Warning);
		}

		public static void Error(string message)
		{
			LogInternal(message, LogLevel.Error);
		}

		private static void LogInternal(string message, LogLevel logLevel)
		{
			try
			{
				if (logLevel >= NyamuSettings.Instance.minLogLevel)
					UnityEngine.Debug.unityLogger.Log(Map(logLevel), message);
			}
			catch (Exception e)
			{
				UnityEngine.Debug.unityLogger.Log(Map(logLevel), message);
				UnityEngine.Debug.LogException(e);
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