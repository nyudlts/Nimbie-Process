using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nimbie_Rename_UI
{
    public class Medialog
    {
        private readonly Action<string> _log;
        private string medialogToken { get; set; }
        private HttpClient client { get; set; }
        private Config config { get; set; }

        public Medialog(Action<string> log)
        {
            _log = log;
            config = Config.GetConfig();
        }

        public async Task PrintHello()
        {
            await Task.Run(() => _log("Hello, World!"));
        }

        public async Task UpdateMedialog(string imageDirectory, string manifestFile)
        {
            var manifestPath = System.IO.Path.Combine(imageDirectory, manifestFile);
            _log($"processing {manifestPath}");
            await SetToken();
            _log($"using token: {medialogToken}");

            foreach (var identifier in File.ReadLines(manifestPath))
            {      
                await UpdateMedialogEntry(imageDirectory, identifier);
            }
            _log("medialog update complete");
        }

        private async Task SetToken()
        {
            client = new HttpClient();  

            //request a token from the Medialog API
            var authRequest = await client.PostAsync($"{config.Host}/api/v0/users/{config.Username}/login?password={config.Password}", null);
            var jsonResponse = await authRequest.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(jsonResponse);
            medialogToken = doc.RootElement.GetProperty("token").GetString();
        }


        private async Task UpdateMedialogEntry(string imageDirectory, string identifier)
        {
            var mediaDirectory = System.IO.Path.Combine(imageDirectory, identifier);
            _log($"updating identifier: {identifier}");
            var (collectionCode, mediaId) = await ParseIdentifier("FA_MSS_646_3");
            _log($"looking up resource {collectionCode}");

            var getResourceIDRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/resources/find/{collectionCode}");
            getResourceIDRequest.Headers.Add("X-Medialog-Token", medialogToken);
            var getResourceIDResponse = await client.SendAsync(getResourceIDRequest);
            var responseJson = await getResourceIDResponse.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);
            var resourceId = doc.RootElement.GetProperty("resource_id").GetUInt32();
            _log($"{resourceId}");


            //get the entryID <- this can be done server side, but for now we will do it client side
            var getEntryMapRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/resources/{resourceId}/entry_and_media_ids");
            getEntryMapRequest.Headers.Add("X-Medialog-Token", medialogToken);
            var getEntryMapResponse = await client.SendAsync(getEntryMapRequest);
            var getEntryMapJson = await getEntryMapResponse.Content.ReadAsStringAsync();
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(getEntryMapJson);
            var entryMatch = entries?.FirstOrDefault(x => x.Value == mediaId.ToString());
            if (entryMatch == null)
            {
                throw new Exception($"No entry found for media ID {mediaId}.");
            }
            var entryId = entryMatch.Value.Key;

            //get the entry
            var getEntryRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/entries/{entryId}");
            getEntryRequest.Headers.Add("X-Medialog-Token", medialogToken);
            var getEntryResponse = await client.SendAsync(getEntryRequest);
            var getEntryJson = await getEntryResponse.Content.ReadAsStringAsync();
            var entry = JsonSerializer.Deserialize<Entry>(getEntryJson);
            entry.IsRefreshed = true;

            var postUpdateEntryRequest = new HttpRequestMessage(HttpMethod.Post, $"https://localhost:8080/api/v0/entries/{entryId}/update");
            postUpdateEntryRequest.Headers.Add("X-Medialog-Token", medialogToken);
            postUpdateEntryRequest.Content = new StringContent(JsonSerializer.Serialize(entry), System.Text.Encoding.UTF8, "application/json");
            var postUpdateEntryResponse = await client.SendAsync(postUpdateEntryRequest);
            var updateEntryJson = await postUpdateEntryResponse.Content.ReadAsStringAsync();
            var updateDoc = JsonDocument.Parse(updateEntryJson);
            _log($"{JsonSerializer.Serialize(updateDoc)}");
        }

        private async Task<(string cId, string mId)> ParseIdentifier(string identifier)
        {
            var parts = identifier.Split('_');
            var resourceID1 = parts[parts.Length - 3].ToLower();
            var resourceID2 = parts[parts.Length - 2];
            var mId = parts[parts.Length - 1];
            return (resourceID1 + resourceID2, mId);
        }
    }
}
