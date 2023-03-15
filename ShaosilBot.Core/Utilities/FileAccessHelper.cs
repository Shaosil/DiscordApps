using Microsoft.Extensions.Configuration;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Utilities
{
	public class FileAccessHelper : IFileAccessHelper
	{
		private string _basePath;
		private HashSet<string> _fileLocks = new HashSet<string>();

		public FileAccessHelper(IConfiguration configuration)
		{
			_basePath = configuration.GetValue<string>("FilesBasePath");
		}

		public string GetFileText(string filename, bool aquireLease = false)
		{
			// Await lease if one exists, and aquire if requested
			lock (_fileLocks)
			{
				while (_fileLocks.Contains(filename))
				{
					Thread.Sleep(250);
				}

				if (aquireLease)
				{
					_fileLocks.Add(filename);
				}
			}

			// Read text from filesystem
			return File.ReadAllText(Path.Combine(_basePath, filename));
		}

		public void ReleaseFileLease(string filename)
		{
			lock (_fileLocks)
			{
				_fileLocks.Remove(filename);
			}
		}

		public void SaveFileText(string filename, string content, bool releaseLease = true)
		{
			// Save file to filesystem
			File.WriteAllText(Path.Combine(_basePath, filename), content);

			// Release lease if any exists by default
			if (releaseLease)
			{
				lock (_fileLocks)
				{
					if (_fileLocks.Contains(filename))
					{
						_fileLocks.Remove(filename);
					}
				}
			}
		}
	}
}