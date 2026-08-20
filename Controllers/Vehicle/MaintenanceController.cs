using CarCareTracker.Helper;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [HttpGet]
        public IActionResult GetMaintenanceImportPartialView(int vehicleId)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), vehicleId, HouseholdPermission.Edit)) return Forbid();
            return PartialView("Reminder/_MaintenanceImport", new MaintenanceImportRequest { VehicleId = vehicleId });
        }

        [HttpGet]
        public IActionResult DownloadMaintenanceTemplate(int vehicleId, MaintenanceImportType type)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), vehicleId, HouseholdPermission.Edit)) return Forbid();
            var content = type == MaintenanceImportType.Schedule
                ? "service_key,name,mileage_interval,month_interval,notes\nengine-oil,Engine Oil and Filter,8000,6,Use the manufacturer-recommended oil\n"
                : "service_key,date,odometer,cost,notes\nengine-oil,2026-01-15,45000,89.99,Imported maintenance history\n";
            return File(Encoding.UTF8.GetBytes(content), "text/csv", $"maintenance-{type.ToString().ToLowerInvariant()}-template.csv");
        }

        [HttpPost]
        public IActionResult PreviewMaintenanceImport(MaintenanceImportRequest request)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), request.VehicleId, HouseholdPermission.Edit)) return Forbid();
            return Json(BuildMaintenanceImport(request, false));
        }

        [HttpPost]
        public IActionResult CommitMaintenanceImport(MaintenanceImportRequest request)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), request.VehicleId, HouseholdPermission.Edit)) return Forbid();
            return Json(BuildMaintenanceImport(request, true));
        }

        private MaintenanceImportPreview BuildMaintenanceImport(MaintenanceImportRequest request, bool commit)
        {
            return request.Type == MaintenanceImportType.Schedule
                ? BuildScheduleImport(request, commit)
                : BuildHistoryImport(request, commit);
        }

        private MaintenanceImportPreview BuildScheduleImport(MaintenanceImportRequest request, bool commit)
        {
            var parsed = MaintenanceImportHelper.ParseSchedule(request.Content);
            var preview = new MaintenanceImportPreview { Rows = parsed.Rows };
            if (preview.Rows.Any(x => x.IsError)) return preview;

            var reminders = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(request.VehicleId);
            var serviceRecords = _serviceRecordDataAccess.GetServiceRecordsByVehicleId(request.VehicleId);
            var duplicateNames = reminders.Where(x => x.IsMaintenance).Select(x => new { Name = x.Description.Trim(), Key = x.ServiceKey })
                .Concat(parsed.Records.Select(x => new { Name = x.Name.Trim(), Key = x.ServiceKey }))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < parsed.Records.Count; index++)
            {
                var import = parsed.Records[index];
                var row = preview.Rows[index];
                var reminder = reminders.FirstOrDefault(x => x.IsMaintenance && x.ServiceKey.Equals(import.ServiceKey, StringComparison.OrdinalIgnoreCase));
                row.Status = reminder is null ? "Add" : "Update";
                row.Message = reminder is null ? "Will add maintenance item" : "Will update maintenance item";
                if (duplicateNames.Contains(import.Name) && serviceRecords.Any(x => string.IsNullOrWhiteSpace(x.ServiceKey) && x.Description.Equals(import.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    row.Status = "Warning";
                    row.Message = "Name is shared by multiple keys; legacy records will not be linked automatically";
                }
                if (!commit) continue;

                reminder ??= new ReminderRecord { VehicleId = request.VehicleId };
                reminder.IsMaintenance = true;
                reminder.ServiceKey = import.ServiceKey;
                reminder.Description = import.Name.Trim();
                reminder.Notes = import.Notes?.Trim() ?? string.Empty;
                reminder.IsRecurring = true;
                reminder.FixedIntervals = false;
                MaintenanceScheduleHelper.SetIntervals(reminder, import.MileageInterval, import.MonthInterval);

                var keyedRecords = serviceRecords.Where(x => x.ServiceKey.Equals(import.ServiceKey, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!duplicateNames.Contains(import.Name))
                {
                    var legacyRecords = MaintenanceScheduleHelper.FindLegacyRecords(serviceRecords, import.Name);
                    foreach (var legacy in legacyRecords)
                    {
                        legacy.ServiceKey = import.ServiceKey;
                        _serviceRecordDataAccess.SaveServiceRecordToVehicle(legacy);
                    }
                    keyedRecords.AddRange(legacyRecords);
                }
                foreach (var record in keyedRecords)
                {
                    record.Description = reminder.Description;
                    _serviceRecordDataAccess.SaveServiceRecordToVehicle(record);
                }
                MaintenanceScheduleHelper.Recalculate(reminder, keyedRecords, _vehicleLogic.GetMaxMileage(request.VehicleId), DateTime.Today);
                _reminderRecordDataAccess.SaveReminderRecordToVehicle(reminder);
                if (!reminders.Contains(reminder)) reminders.Add(reminder);
            }
            return preview;
        }

        private MaintenanceImportPreview BuildHistoryImport(MaintenanceImportRequest request, bool commit)
        {
            var parsed = MaintenanceImportHelper.ParseHistory(request.Content);
            var preview = new MaintenanceImportPreview { Rows = parsed.Rows };
            if (preview.Rows.Any(x => x.IsError)) return preview;

            var reminders = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(request.VehicleId);
            var serviceRecords = _serviceRecordDataAccess.GetServiceRecordsByVehicleId(request.VehicleId);
            var affectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < parsed.Records.Count; index++)
            {
                var import = parsed.Records[index];
                var row = preview.Rows[index];
                var date = DateTime.ParseExact(import.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var mileage = import.Odometer ?? 0;
                var duplicate = MaintenanceScheduleHelper.IsDuplicateHistory(serviceRecords, import.ServiceKey, date, mileage);
                if (duplicate)
                {
                    row.Status = "Skip";
                    row.Message = "Matching history record already exists";
                    continue;
                }
                var reminder = reminders.FirstOrDefault(x => x.IsMaintenance && x.ServiceKey.Equals(import.ServiceKey, StringComparison.OrdinalIgnoreCase));
                row.Status = reminder is null ? "Warning" : "Add";
                row.Message = reminder is null ? "Will import now and link when its schedule is added" : "Will add service history";
                if (!commit) continue;

                var record = new ServiceRecord
                {
                    VehicleId = request.VehicleId,
                    ServiceKey = import.ServiceKey,
                    Date = date,
                    Mileage = mileage,
                    Cost = import.Cost ?? 0,
                    Description = reminder?.Description ?? HumanizeServiceKey(import.ServiceKey),
                    Notes = import.Notes?.Trim() ?? string.Empty
                };
                _serviceRecordDataAccess.SaveServiceRecordToVehicle(record);
                serviceRecords.Add(record);
                affectedKeys.Add(import.ServiceKey);
            }
            if (commit)
            {
                foreach (var key in affectedKeys)
                {
                    var reminder = reminders.FirstOrDefault(x => x.IsMaintenance && x.ServiceKey.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (reminder is null) continue;
                    MaintenanceScheduleHelper.Recalculate(reminder, serviceRecords.Where(x => x.ServiceKey.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList(), _vehicleLogic.GetMaxMileage(request.VehicleId), DateTime.Today);
                    _reminderRecordDataAccess.SaveReminderRecordToVehicle(reminder);
                }
            }
            return preview;
        }

        private static string HumanizeServiceKey(string serviceKey) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(serviceKey.Replace('-', ' '));
    }
}
