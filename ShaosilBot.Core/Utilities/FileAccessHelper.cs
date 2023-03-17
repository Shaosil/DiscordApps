using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Utilities
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

		public string GetFileText(string filename, bool acquireLease = false)
		{
			_logger.LogInformation($"Entered {nameof(GetFileText)}: filename={filename} aquireLease={acquireLease}");

			// Await lease if one exists, and aquire if requested
			lock (_fileLocks)
			{
				if (_fileLocks.Contains(filename)) _logger.LogInformation("File locked... waiting...");
				while (_fileLocks.Contains(filename))
				{
					Thread.Sleep(250);
				}

				if (acquireLease)
				{
					if (_fileLocks.Contains(filename)) _logger.LogInformation("Locking file.");
					_fileLocks.Add(filename);
				}
			}

			// Read text from filesystem
			_logger.LogInformation("Reading file text.");
			return File.ReadAllText(Path.Combine(_basePath, filename));
		}

		public void ReleaseFileLease(string filename)
		{
			_logger.LogInformation($"Entered {nameof(ReleaseFileLease)}: filename={filename}");

			lock (_fileLocks)
			{
				_logger.LogInformation("Unlocking file");
				_fileLocks.Remove(filename);
			}
		}

		public void SaveFileText(string filename, string content, bool releaseLease = true)
		{
			_logger.LogInformation($"Entered {nameof(SaveFileText)}: filename={filename} releaseLease={releaseLease}");

			// Save file to filesystem
			File.WriteAllText(Path.Combine(_basePath, filename), content);

			// Release lease if any exists by default
			if (releaseLease)
			{
				lock (_fileLocks)
				{
					if (_fileLocks.Contains(filename))
					{
						_logger.LogInformation("Unlocking file");
						_fileLocks.Remove(filename);
					}
				}
			}
		}
	}
}