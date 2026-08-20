using System.Text.Json.Serialization;
using CsvHelper.Configuration.Attributes;

namespace CarCareTracker.Models
{
    public enum MaintenanceImportType
    {
        Schedule,
        History
    }

    public class MaintenanceImportRequest
    {
        public int VehicleId { get; set; }
        public MaintenanceImportType Type { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class MaintenanceScheduleImport
    {
        [JsonPropertyName("service_key"), Name("service_key")]
        public string ServiceKey { get; set; } = string.Empty;
        [JsonPropertyName("name"), Name("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("mileage_interval"), Name("mileage_interval")]
        public int? MileageInterval { get; set; }
        [JsonPropertyName("month_interval"), Name("month_interval")]
        public int? MonthInterval { get; set; }
        [JsonPropertyName("notes"), Name("notes")]
        public string Notes { get; set; } = string.Empty;
    }

    public class MaintenanceHistoryImport
    {
        [JsonPropertyName("service_key"), Name("service_key")]
        public string ServiceKey { get; set; } = string.Empty;
        [JsonPropertyName("date"), Name("date")]
        public string Date { get; set; } = string.Empty;
        [JsonPropertyName("odometer"), Name("odometer")]
        public int? Odometer { get; set; }
        [JsonPropertyName("cost"), Name("cost")]
        public decimal? Cost { get; set; }
        [JsonPropertyName("notes"), Name("notes")]
        public string Notes { get; set; } = string.Empty;
    }

    public class MaintenanceImportRowResult
    {
        public int Row { get; set; }
        public string ServiceKey { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    public class MaintenanceImportPreview
    {
        public bool Success => Rows.Any() && Rows.All(x => !x.IsError);
        public List<MaintenanceImportRowResult> Rows { get; set; } = new List<MaintenanceImportRowResult>();
    }

    public class MaintenanceParsedImport<T>
    {
        public List<T> Records { get; set; } = new List<T>();
        public List<MaintenanceImportRowResult> Rows { get; set; } = new List<MaintenanceImportRowResult>();
    }
}
