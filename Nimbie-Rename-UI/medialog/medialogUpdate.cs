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
        private string? MedialogToken { get; set; }
        private HttpClient Client { get; set; }
        private Config AppConfig { get; set; }
        private string? NimbieUser { get; set; }
        private string? ImageDirectory { get; set; }
        public Medialog(Action<string> log)
        {
            _log = log;
            AppConfig = Config.GetConfig();
            Client = new HttpClient();
        }

        public async Task PrintHello()
        {
            await Task.Run(() => _log("Hello, World!"));
        }

        public async Task UpdateMedialog(string imgDirectory, string nimbieUsername)
        {
            NimbieUser = nimbieUsername;
            ImageDirectory = imgDirectory;

            try
            {
                await SetToken();
            }
            catch (Exception ex)
            {
                _log($"ERROR: {ex.Message}");
            }
            _log($"using token: {MedialogToken}");

            foreach (var mediaType in new string[] {"audio","data","video"})
            {
              
               
                var mediaTypePath = Path.Combine(ImageDirectory, mediaType);
               
                
                if (Directory.Exists(mediaTypePath))
                {
                    
                    var mediaDirectories = Directory.GetDirectories(mediaTypePath);
                    foreach (var mediaDirectory in mediaDirectories)
                    {
                        var mediaId = Path.GetFileName(mediaDirectory);
                        
                        string imageFile;
                        if (mediaType == "audio")
                        {       
                            imageFile = mediaId + ".wav";
                        }
                        else
                        {
                            imageFile = Path.Combine(mediaId + ".iso");
                        }
                        _log($"type: {mediaType}, mediaId: {mediaId}, imageFile: {imageFile}");
                        
                        await UpdateMedialogEntry(mediaId, imageFile, mediaType);
                    }
                    
                } else
                {
                    _log($"{mediaTypePath} does not exist");
                }
                

            }
        }

        private async Task SetToken()
        {
            //request a token from the Medialog API
            var authRequest = await Client.PostAsync($"{AppConfig.Host}/api/v0/users/{AppConfig.Username}/login?password={AppConfig.Password}", null);
            var jsonResponse = await authRequest.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(jsonResponse);
            MedialogToken = doc.RootElement.GetProperty("token").GetString();
        }


        private async Task UpdateMedialogEntry(string mediaId, string filename, string mediaType)
        {
            try
            {
                var (collectionCode, mediaNum) = ParseIdentifier(mediaId);
                UInt32 resourceId = await getResourceId(collectionCode);
                _log($"resourceId: {resourceId}");
                string entryId = await GetEntryId(resourceId, mediaNum);
                _log($"entryId: {entryId}");
                Entry entry = await getEntry(entryId);
                _log($"entry: {JsonSerializer.Serialize(entry)}");

                //update logic 
                entry.IsRefreshed = true;
                entry.ImageFilename = filename;


                switch (mediaType)
                {
                    case "audio":
                        entry.ImageFormat = "image_format_wavcue";
                        entry.ContentType = "content_audio";
                        break;
                    case "video":
                        entry.ImageFormat = "image_format_iso";
                        entry.ContentType = "content_video";
                        break;
                    case "data":
                        entry.ImageFormat = "image_format_iso";
                        entry.ContentType = "content_data";
                        break;
                    default:
                        break;
                }

                entry.Location = "sl_rsw_acm_born_digital";
                entry.Interface = "interface_optical_NIMBIE";
                entry.ImagingSoftware = "imaging_software_imgburn";
                entry.ImagingSuccess = "image_success_yes";
                entry.HddInterface = "hdd_interface_usb";
                entry.ImagedBy = NimbieUser!;

                var imagePath = Path.Join(ImageDirectory, mediaType, mediaId, filename);
                _log(imagePath);
                _log(File.Exists(imagePath).ToString());
                long size = new FileInfo(imagePath).Length;
                entry.PhysicalSize = size;


                _log($"updated entry: {JsonSerializer.Serialize(entry)}");

                //update the entry
                var updateResponse = await postUpdatedEntry(entry);
                _log($"response: {updateResponse}");
            }
            catch (Exception ex)
            {
                _log($"ERROR: {ex.Message}");
            }
        }

        private (string cId, string mId) ParseIdentifier(string identifier)
        {
            var parts = identifier.Split('_');
            var resourceID1 = parts[parts.Length - 3].ToLower();
            var resourceID2 = parts[parts.Length - 2];
            var mId = parts[parts.Length - 1];
            return (resourceID1 + resourceID2, mId);
        }


        private async Task<UInt32> getResourceId(string collectionCode)
        {
            
            var getResourceIDRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/resources/find/{collectionCode}");
            getResourceIDRequest.Headers.Add("X-Medialog-Token", MedialogToken);
            var getResourceIDResponse = await Client.SendAsync(getResourceIDRequest);
            var responseJson = await getResourceIDResponse.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement.GetProperty("resource_id").GetUInt32();
        }
        
        private async Task<string> GetEntryId(UInt32 resourceId, string mediaId)
        {
            //get the entryID <- this can be done server side, but for now we will do it client side
            var getEntryMapRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/resources/{resourceId}/entry_and_media_ids");
            getEntryMapRequest.Headers.Add("X-Medialog-Token", MedialogToken);
            var getEntryMapResponse = await Client.SendAsync(getEntryMapRequest);
            var getEntryMapJson = await getEntryMapResponse.Content.ReadAsStringAsync();
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(getEntryMapJson);
            var entryMatch = entries?.FirstOrDefault(x => x.Value == mediaId)
                ?? throw new Exception($"No entry found for media ID {mediaId}.");
            return entryMatch.Key;
        }

        private async Task<Entry> getEntry(string entryId)
        {
            var getEntryRequest = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:8080/api/v0/entries/{entryId}");
            getEntryRequest.Headers.Add("X-Medialog-Token", MedialogToken);
            var getEntryResponse = await Client.SendAsync(getEntryRequest);
            var getEntryJson = await getEntryResponse.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<Entry>(getEntryJson)
                ?? throw new Exception($"could not deserialize entry {entryId}");
            return response;
        }

        private async Task<string> postUpdatedEntry(Entry entry)
        {
            var postUpdateEntryRequest = new HttpRequestMessage(HttpMethod.Post, $"https://localhost:8080/api/v0/entries/{entry.Id}/update");
            postUpdateEntryRequest.Headers.Add("X-Medialog-Token", MedialogToken);
            postUpdateEntryRequest.Content = new StringContent(JsonSerializer.Serialize(entry), System.Text.Encoding.UTF8, "application/json");
            var postUpdateEntryResponse = await Client.SendAsync(postUpdateEntryRequest);
            var updateEntryJson = await postUpdateEntryResponse.Content.ReadAsStringAsync();
            var updateDoc = JsonDocument.Parse(updateEntryJson)
                ?? throw new Exception($"could not post entry {entry.Id}");
            return updateDoc.RootElement.ToString();
        }
    }
}
