using System;
using System.Text.Json.Serialization;

namespace Nimbie_Rename_UI
{
    public class Entry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("created_at")]
        public DateTime Created_at { get; set; }
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
        [JsonPropertyName("created_by")]
        public int CreatedBy { get; set; }
        [JsonPropertyName("updated_by")]
        public int UpdatedBy { get; set; }
        [JsonPropertyName("media_id")]
        public int MediaId { get; set; }
        [JsonPropertyName("mediatype")]
        public string Mediatype { get; set; }
        [JsonPropertyName("manufacturer")]
        public string Manufacturer { get; set; }
        [JsonPropertyName("manufacturer_serial")]
        public string ManufacturerSerial { get; set; }
        [JsonPropertyName("label_text")]
        public string LabelText { get; set; }
        [JsonPropertyName("media_note")]
        public string MediaNote { get; set; }
        [JsonPropertyName("hdd_interface")]
        public string HddInterface { get; set; }
        [JsonPropertyName("imaging_success")]
        public string? ImagingSuccess { get; set; }
        [JsonPropertyName("image_filename")]
        public string ImageFilename { get; set; }
        [JsonPropertyName("interface")]
        public string Interface { get; set; }
        [JsonPropertyName("imaging_software")]
        public string ImagingSoftware { get; set; }
        [JsonPropertyName("interpretation_success")]
        public string InterpretationSuccess { get; set; }
        [JsonPropertyName("imaged_by")]
        public string ImagedBy { get; set; }
        [JsonPropertyName("imaging_note")]
        public string ImagingNote { get; set; }
        [JsonPropertyName("image_format")]
        public string ImageFormat { get; set; }
        [JsonPropertyName("box_number")]
        public string BoxNumber { get; set; }
        [JsonPropertyName("original_id")]
        public string OriginalId { get; set; }
        [JsonPropertyName("disposition_note")]
        public string DispositionNote { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("stock_unit")]
        public string StockUnit { get; set; }
        [JsonPropertyName("stock_size_num")]
        public float StockSizeNum { get; set; }
        [JsonPropertyName("repository_id")]
        public int RepositoryId { get; set; }
        [JsonPropertyName("resource_id")]
        public int ResourceId { get; set; }
        [JsonPropertyName("accession_id")]
        public int AccessionId { get; set; }
        [JsonPropertyName("is_refreshed")]
        public bool IsRefreshed { get; set; }
        [JsonPropertyName("is_transferred")]
        public bool IsTransferred { get; set; }
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; }
        [JsonPropertyName("structure")]
        public string Structure { get; set; }
        [JsonPropertyName("location")]
        public string Location { get; set; }



    }
}