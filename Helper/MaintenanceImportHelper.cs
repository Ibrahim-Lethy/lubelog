using CarCareTracker.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarCareTracker.Helper
{
    public static partial class MaintenanceImportHelper
    {
        public const int MaxBytes = 1024 * 1024;
        public const int MaxRows = 1000;
        public static bool IsValidServiceKey(string serviceKey) => ServiceKeyRegex().IsMatch(serviceKey);

        public static MaintenanceParsedImport<MaintenanceScheduleImport> ParseSchedule(string content)
        {
            var result = Parse<MaintenanceScheduleImport>(content);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Records.Count; index++)
            {
                var record = result.Records[index];
                record.ServiceKey = record.ServiceKey.Trim().ToLowerInvariant();
                var errors = new List<string>();
                if (!ServiceKeyRegex().IsMatch(record.ServiceKey)) errors.Add("service_key must be a lowercase slug such as engine-oil");
                if (string.IsNullOrWhiteSpace(record.Name)) errors.Add("name is required");
                if (record.MileageInterval is <= 0) errors.Add("mileage_interval must be positive");
                if (record.MonthInterval is <= 0) errors.Add("month_interval must be positive");
                if (record.MileageInterval is null && record.MonthInterval is null) errors.Add("at least one interval is required");
                if (!seen.Add(record.ServiceKey)) errors.Add("service_key is duplicated in this import");
                AddValidationRow(result.Rows, index, record.ServiceKey, errors);
            }
            return result;
        }

        public static MaintenanceParsedImport<MaintenanceHistoryImport> ParseHistory(string content)
        {
            var result = Parse<MaintenanceHistoryImport>(content);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Records.Count; index++)
            {
                var record = result.Records[index];
                record.ServiceKey = record.ServiceKey.Trim().ToLowerInvariant();
                var errors = new List<string>();
                if (!ServiceKeyRegex().IsMatch(record.ServiceKey)) errors.Add("service_key must be a lowercase slug such as engine-oil");
                if (!DateTime.TryParseExact(record.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) errors.Add("date must use YYYY-MM-DD");
                if (record.Odometer < 0) errors.Add("odometer cannot be negative");
                if (record.Cost < 0) errors.Add("cost cannot be negative");
                if (!seen.Add($"{record.ServiceKey}|{record.Date}|{record.Odometer}")) errors.Add("service_key, date, and odometer are duplicated in this import");
                AddValidationRow(result.Rows, index, record.ServiceKey, errors);
            }
            return result;
        }

        private static MaintenanceParsedImport<T> Parse<T>(string content)
        {
            var result = new MaintenanceParsedImport<T>();
            if (string.IsNullOrWhiteSpace(content))
            {
                result.Rows.Add(Error(0, "", "Import content is empty"));
                return result;
            }
            if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
            {
                result.Rows.Add(Error(0, "", "Import exceeds the 1 MB limit"));
                return result;
            }
            try
            {
                if (content.TrimStart().StartsWith("["))
                {
                    result.Records = JsonSerializer.Deserialize<List<T>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                }
                else
                {
                    using var reader = new StringReader(content);
                    var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        MissingFieldFound = null,
                        HeaderValidated = null,
                        PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
                    };
                    using var csv = new CsvReader(reader, config);
                    result.Records = csv.GetRecords<T>().ToList();
                }
            }
            catch (Exception ex)
            {
                result.Rows.Add(Error(0, "", $"Unable to parse import: {ex.Message}"));
                return result;
            }
            if (result.Records.Count == 0) result.Rows.Add(Error(0, "", "Import contains no rows"));
            if (result.Records.Count > MaxRows)
            {
                result.Records.Clear();
                result.Rows.Add(Error(0, "", $"Import exceeds the {MaxRows:N0} row limit"));
            }
            return result;
        }

        private static void AddValidationRow(List<MaintenanceImportRowResult> rows, int index, string serviceKey, List<string> errors)
        {
            rows.Add(new MaintenanceImportRowResult
            {
                Row = index + 1,
                ServiceKey = serviceKey,
                Status = errors.Any() ? "Error" : "Valid",
                Message = errors.Any() ? string.Join("; ", errors) : "Ready",
                IsError = errors.Any()
            });
        }

        private static MaintenanceImportRowResult Error(int row, string key, string message) => new()
        {
            Row = row,
            ServiceKey = key,
            Status = "Error",
            Message = message,
            IsError = true
        };

        [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        private static partial Regex ServiceKeyRegex();
    }
}
