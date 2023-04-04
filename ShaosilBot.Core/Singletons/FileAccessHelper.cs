using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Text.Json;

namespace ShaosilBot.Core.Singletons
{
	public class FileAccessHelper : IFileAccessHelper
	{
		private readonly ILogger<FileAccessHelper> _logger;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private string _basePath;
		private Dictionary<string, ManualResetEventSlim> _fileLocks = new Dictionary<string, ManualResetEventSlim>();
		private int _lockWaitMaxMs = 10000;

		public FileAccessHelper(ILogger<FileAccessHelper> logger, IConfiguration configuration, IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_restClientProvider = restClientProvider;
			_basePath = configuration["FilesBasePath"]!.ToString();
		}

		public T LoadFileJSON<T>(string fileName, bool lockFile = false) where T : new()
		{
			_logger.LogInformation($"Entered {nameof(LoadFileJSON)}: filename={fileName} lockFile={lockFile}");
			string contents = GetTextFromFile(fileName, lockFile, () => JsonSerializer.Serialize(new T()));
			return JsonSerializer.Deserialize<T>(contents)!;
		}

		public string LoadFileText(string fileName, bool lockFile = false)
		{
			_logger.LogInformation($"Entered {nameof(LoadFileText)}: filename={fileName} keepLease={lockFile}");
			return GetTextFromFile(fileName, lockFile, () => string.Empty);
		}

		public void SaveFileJSON<T>(string filename, T content, bool releaseLease = true)
		{
			_logger.LogInformation($"Entered {nameof(SaveFileJSON)}: filename={filename} releaseLease={releaseLease}");

			// Save file to filesystem
			string fullPath = Path.Combine(_basePath, filename);
			lock (_fileLocks)
			{
				File.WriteAllText(fullPath, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
			}

			// Release lease if any exists by default
			if (releaseLease)
			{
				ReleaseFileLease(filename);
			}
		}

		private string GetTextFromFile(string fileName, bool lockFile, Func<string> defaultContentFunc)
		{
			string fullPath = Path.Combine(_basePath, fileName);

			// Add file path to dictionary if it doesn't exist yet and immediately allow this thread to work on it
			lock (_fileLocks)
			{
				if (!_fileLocks.ContainsKey(fullPath))
				{
					_fileLocks[fullPath] = new ManualResetEventSlim(true);
				}
			}

			// Wait here if another thread has a lock on the requested file
			if (!_fileLocks[fullPath].IsSet) _logger.LogInformation("File locked... waiting...");
			if (!_fileLocks[fullPath].Wait(_lockWaitMaxMs))
			{
				// If the file is still locked after the wait timeout, just proceed, log and notify Shaosil
				string message = $"{nameof(FileAccessHelper)} encountered an apparent deadlock after {_lockWaitMaxMs / 1000} seconds and continued. Locked file: {fullPath}";
				_logger.LogWarning(message);
				_restClientProvider.DMShaosil(message);
			}

			// Immediately lock this file if requested
			if (lockFile)
			{
				_logger.LogInformation("Locking file.");
				_fileLocks[fullPath].Reset();
			}

			// If the file doesn't exist, created it from the default while we have the lock
			if (!new FileInfo(fullPath).Exists) File.WriteAllText(fullPath, defaultContentFunc());

			_logger.LogInformation("Reading file contents.");
			return File.ReadAllText(fullPath);
		}

		public void ReleaseFileLease(string fileName)
		{
			_logger.LogInformation($"Entered {nameof(ReleaseFileLease)}: filename={fileName}");
			string fullPath = Path.Combine(_basePath, fileName);

			// Release lock if one exists
			lock (_fileLocks)
			{
				if (_fileLocks.ContainsKey(fullPath))
				{
					_logger.LogInformation("Releasing file lock.");
					_fileLocks[fullPath].Set();
				}
			}
		}
	}
}