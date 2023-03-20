using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Text.Json;

namespace ShaosilBot.Core.Singletons
{
	public class FileAccessHelper : IFileAccessHelper
	{
		private readonly ILogger<FileAccessHelper> _logger;
		private string _basePath;
		private HashSet<string> _fileLocks = new HashSet<string>();

		public FileAccessHelper(ILogger<FileAccessHelper> logger, IConfiguration configuration)
		{
			_logger = logger;
			_basePath = configuration.GetValue<string>("FilesBasePath");
		}

		public T LoadFileJSON<T>(string filename, bool keepLease = false) where T : new()
		{
			_logger.LogInformation($"Entered {nameof(LoadFileJSON)}: filename={filename} keepLease={keepLease}");
			string fullPath = Path.Combine(_basePath, filename);

			PrepForReading(fullPath, keepLease, () => JsonSerializer.Serialize(new T()));

			// Read text from filesystem
			_logger.LogInformation("Reading file contents.");
			return JsonSerializer.Deserialize<T>(File.ReadAllText(fullPath))!;
		}

		public string LoadFileText(string filename, bool keepLease = false)
		{
			_logger.LogInformation($"Entered {nameof(LoadFileText)}: filename={filename} keepLease={keepLease}");
			string fullPath = Path.Combine(_basePath, filename);

			PrepForReading(fullPath, keepLease, () => JsonSerializer.Serialize(string.Empty));

			// Read text from filesystem
			_logger.LogInformation("Reading file text.");
			return File.ReadAllText(fullPath);
		}

		public void ReleaseFileLease(string fullPath)
		{
			_logger.LogInformation($"Entered {nameof(ReleaseFileLease)}: filename={fullPath}");

			lock (_fileLocks)
			{
				_logger.LogInformation("Unlocking file");
				_fileLocks.Remove(fullPath);
			}
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
				ReleaseFileLease(fullPath);
			}
		}

		private void PrepForReading(string fullPath, bool keepLease, Func<string> defaultContentFunc)
		{
			// Await lease if one exists, and aquire if requested
			lock (_fileLocks)
			{
				if (_fileLocks.Contains(fullPath)) _logger.LogInformation("File locked... waiting...");
				while (_fileLocks.Contains(fullPath))
				{
					Thread.Sleep(250);
				}

				if (keepLease)
				{
					if (_fileLocks.Contains(fullPath)) _logger.LogInformation("Locking file.");
					_fileLocks.Add(fullPath);
				}

				// If the file doesn't exist, created it from the default while we have the lock
				if (!new FileInfo(fullPath).Exists) File.WriteAllText(fullPath, defaultContentFunc());
			}
		}
	}
}