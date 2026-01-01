using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace Nyamu.Core
{
	/// <summary>
	/// Manages global registry of Unity projects using Nyamu and their assigned ports.
	/// Registry is stored in user profile directory to persist across Unity sessions.
	/// </summary>
	public static class NyamuProjectRegistry
	{
		const int DefaultStartPort = 17942;
		const int MaxPortSearchRange = 100;
		const int MinValidPort = 1024;
		const int MaxValidPort = 65535;

		static readonly object _registryLock = new object();

		static string RegistryPath => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".nyamu",
			"NyamuProjectsRegistry.json"
		);

		[System.Serializable]
		public class ProjectEntry
		{
			public string projectPath;
			public int port;
			public string registeredAt;
			public string projectName;
		}

		[System.Serializable]
		public class RegistryData
		{
			public List<ProjectEntry> projectEntries = new List<ProjectEntry>();
			public string version = "1.0";
		}

		/// <summary>
		/// Find the next available free port starting from startPort.
		/// Checks both registry and actual TCP/IP availability.
		/// </summary>
		public static int FindFreePort(int startPort = DefaultStartPort)
		{
			var currentPort = startPort;
			var maxPort = Math.Min(startPort + MaxPortSearchRange, MaxValidPort);
			var currentProjectPath = GetCurrentProjectPath();

			while (currentPort <= maxPort)
			{
				if (currentPort < MinValidPort)
				{
					currentPort++;
					continue;
				}

				if (IsPortAvailable(currentPort, currentProjectPath))
					return currentPort;

				currentPort++;
			}

			NyamuLogger.LogWarning(
				$"[Nyamu][Registry] No free ports found in range {startPort}-{maxPort}. " +
				$"Using fallback port {startPort}. Server may fail to start.");

			return startPort;
		}

		/// <summary>
		/// Check if a specific port is available for use.
		/// Returns true if port is not in registry (for other projects) AND can be bound via TCP/IP.
		/// </summary>
		public static bool IsPortAvailable(int port, string excludeProjectPath = null)
		{
			if (port < MinValidPort || port > MaxValidPort)
				return false;

			excludeProjectPath = excludeProjectPath ?? GetCurrentProjectPath();

			if (IsPortInRegistryForOtherProject(port, excludeProjectPath))
				return false;

			return CanBindPort(port);
		}

		/// <summary>
		/// Update the registry with current project's port.
		/// Creates or updates the entry for this project.
		/// </summary>
		public static void RegisterProjectPort(int port)
		{
			try
			{
				lock (_registryLock)
				{
					var registry = LoadRegistry();
					var projectPath = GetCurrentProjectPath();
					var projectName = Path.GetFileName(projectPath);

					var existingEntry = registry.projectEntries.FirstOrDefault(e => e.projectPath == projectPath);

					if (existingEntry != null)
					{
						// Project already registered - update port only, keep registeredAt
						existingEntry.port = port;
						existingEntry.projectName = projectName;
					}
					else
					{
						// New project - add entry with current timestamp
						registry.projectEntries.Add(new ProjectEntry
						{
							projectPath = projectPath,
							port = port,
							registeredAt = DateTime.Now.ToString("o"),
							projectName = projectName
						});
					}

					SaveRegistry(registry);
					NyamuLogger.LogInfo($"[Nyamu][Registry] Registered project '{projectName}' with port {port}");
				}
			}
			catch (Exception ex)
			{
				NyamuLogger.LogError($"[Nyamu][Registry] Failed to register project port: {ex.Message}");
			}
		}

		/// <summary>
		/// Get registered port for current project, if exists.
		/// </summary>
		public static int? GetRegisteredPort(string projectPath = null)
		{
			try
			{
				projectPath = projectPath ?? GetCurrentProjectPath();
				var registry = LoadRegistry();
				var entry = registry.projectEntries.FirstOrDefault(e => e.projectPath == projectPath);

				return entry?.port;
			}
			catch (Exception ex)
			{
				NyamuLogger.LogWarning($"[Nyamu][Registry] Failed to get registered port: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Get registry data for display purposes.
		/// </summary>
		public static RegistryData GetRegistryData()
		{
			try
			{
				return LoadRegistry();
			}
			catch
			{
				return new RegistryData();
			}
		}

		static RegistryData LoadRegistry()
		{
			try
			{
				if (File.Exists(RegistryPath))
				{
					var json = File.ReadAllText(RegistryPath);
					var registry = JsonUtility.FromJson<RegistryData>(json);

					if (registry == null || registry.projectEntries == null)
						throw new InvalidDataException("Registry data is null or incomplete");

					return registry;
				}
			}
			catch (Exception ex)
			{
				NyamuLogger.LogWarning(
					$"[Nyamu][Registry] Failed to load registry: {ex.Message}. Creating new registry.");

				if (File.Exists(RegistryPath))
				{
					var backupPath = RegistryPath + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
					try
					{
						File.Copy(RegistryPath, backupPath);
						NyamuLogger.LogInfo($"[Nyamu][Registry] Backed up corrupt registry to {backupPath}");
					}
					catch { }
				}
			}

			return new RegistryData();
		}

		static void SaveRegistry(RegistryData registry)
		{
			const int maxRetries = 3;
			const int retryDelayMs = 100;

			for (var attempt = 0; attempt < maxRetries; attempt++)
			{
				try
				{
					EnsureRegistryDirectory();

					var json = JsonUtility.ToJson(registry, true);
					var tempPath = RegistryPath + ".tmp";

					File.WriteAllText(tempPath, json);

					if (File.Exists(RegistryPath))
						File.Delete(RegistryPath);

					File.Move(tempPath, RegistryPath);

					NyamuLogger.LogDebug($"[Nyamu][Registry] Saved registry to {RegistryPath}");
					return;
				}
				catch (IOException ex) when (attempt < maxRetries - 1)
				{
					NyamuLogger.LogWarning(
						$"[Nyamu][Registry] Save attempt {attempt + 1} failed: {ex.Message}. Retrying...");
					System.Threading.Thread.Sleep(retryDelayMs);
				}
				catch (Exception ex)
				{
					NyamuLogger.LogError($"[Nyamu][Registry] Failed to save registry: {ex.Message}");
					return;
				}
			}
		}

		static void EnsureRegistryDirectory()
		{
			try
			{
				var directory = Path.GetDirectoryName(RegistryPath);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
					Directory.CreateDirectory(directory);
			}
			catch (Exception ex)
			{
				NyamuLogger.LogError(
					$"[Nyamu][Registry] Cannot create registry directory: {ex.Message}. " +
					"Port assignment features may not work correctly.");
			}
		}

		static string GetCurrentProjectPath()
		{
			return Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
		}

		static bool CanBindPort(int port)
		{
			try
			{
				var listener = new TcpListener(IPAddress.Loopback, port);
				listener.Start();
				listener.Stop();
				return true;
			}
			catch (SocketException)
			{
				return false;
			}
			catch (Exception ex)
			{
				NyamuLogger.LogWarning($"[Nyamu][Registry] Unexpected error checking port {port}: {ex.Message}");
				return false;
			}
		}

		static bool IsPortInRegistryForOtherProject(int port, string excludeProjectPath)
		{
			var registry = LoadRegistry();

			foreach (var entry in registry.projectEntries)
			{
				if (entry.projectPath != excludeProjectPath && entry.port == port)
					return true;
			}

			return false;
		}
	}
}
