using System;
using System.IO;
using Newtonsoft.Json;
using WinASM65.Segments;

namespace WinASM65.Core
{
    public interface IConfigurationReader
    {
        ConfigFile Read(string path);
    }

    public class JsonConfigurationReader : IConfigurationReader
    {
        public ConfigFile Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Configuration file path is required.", "path");
            if (!File.Exists(path))
                throw new FileNotFoundException("Configuration file not found.", path);

            using (StreamReader reader = File.OpenText(path))
            {
                JsonSerializer serializer = new JsonSerializer();
                return (ConfigFile)serializer.Deserialize(reader, typeof(ConfigFile));
            }
        }
    }
}
