
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nimbie_Rename_UI
{
    public class Config
    {
        [JsonPropertyName("host")]
        public string? Host { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        public static Config GetConfig()
        {

            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string filePath = Path.Combine(homeDir, "nimbie-config.json");
            string json = File.ReadAllText(filePath);
            Config config = JsonSerializer.Deserialize<Config>(json)!;
            return config;
        }
    }
}