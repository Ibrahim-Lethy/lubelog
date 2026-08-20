using CarCareTracker.Filter;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetServiceRecordsByVehicleId(int vehicleId)
        {
            var result = _serviceRecordDataAccess.GetServiceRecordsByVehicleId(vehicleId);
            bool _useDescending = _config.GetUserConfig(User).UseDescending;
            if (_useDescending)
            {
                result = result.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).ToList();
            }
            else
            {
                result = result.OrderBy(x => x.Date).ThenBy(x => x.Mileage).ToList();
            }
            return PartialView("Service/_ServiceRecords", result);
        }
        [HttpPost]
        public IActionResult SaveServiceRecordToVehicleId(ServiceRecordInput serviceRecord)
        {
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), serviceRecord.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            //move files from temp.
            serviceRecord.Files = serviceRecord.Files.Select(x => { return new UploadedFiles { Name = x.Name, Location = _fileHelper.MoveFileFromTemp(x.Location, "documents/") }; }).ToList();
            if (serviceRecord.Supplies.Any())
            {
                serviceRecord.RequisitionHistory.AddRange(RequisitionSupplyRecordsByUsage(serviceRecord.Supplies, DateTime.Parse(serviceRecord.Date), serviceRecord.Description));
                if (serviceRecord.CopySuppliesAttachment)
                {
                    serviceRecord.Files.AddRange(GetSuppliesAttachments(serviceRecord.Supplies));
                }
            }
            if (serviceRecord.DeletedRequisitionHistory.Any())
            {
                _vehicleLogic.RestoreSupplyRecordsByUsage(serviceRecord.DeletedRequisitionHistory, serviceRecord.Description);
            }
            var previousServiceKey = serviceRecord.Id == default ? string.Empty : _serviceRecordDataAccess.GetServiceRecordById(serviceRecord.Id).ServiceKey;
            ReminderRecord? maintenanceItem = null;
            if (!string.IsNullOrWhiteSpace(serviceRecord.ServiceKey))
            {
                maintenanceItem = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(serviceRecord.VehicleId)
                    .FirstOrDefault(x => x.IsMaintenance && x.ServiceKey.Equals(serviceRecord.ServiceKey, StringComparison.OrdinalIgnoreCase));
                if (maintenanceItem is not null)
                {
                    serviceRecord.Description = maintenanceItem.Description;
                }
            }
            var convertedRecord = serviceRecord.ToServiceRecord();
            var result = _serviceRecordDataAccess.SaveServiceRecordToVehicle(convertedRecord);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromGenericRecord(convertedRecord, serviceRecord.Id == default ? "servicerecord.add" : "servicerecord.update", User.Identity?.Name ?? string.Empty));
                foreach (var serviceKey in new[] { previousServiceKey, convertedRecord.ServiceKey }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    RecalculateMaintenanceItem(serviceRecord.VehicleId, serviceKey);
                }
                foreach (int reminderRecordId in serviceRecord.ReminderRecordId.Where(x => x != maintenanceItem?.Id))
                {
                    PushbackRecurringReminderRecordWithChecks(reminderRecordId, DateTime.Parse(serviceRecord.Date), serviceRecord.Mileage);
                }
            }
            if (convertedRecord.Id != default && serviceRecord.Id == default && _config.GetUserConfig(User).EnableAutoOdometerInsert)
            {
                _odometerLogic.AutoInsertOdometerRecord(new OdometerRecord
                {
                    Date = DateTime.Parse(serviceRecord.Date),
                    VehicleId = serviceRecord.VehicleId,
                    Mileage = serviceRecord.Mileage,
                    Notes = $"{_translator.Translate(_config.GetUserConfig(User).UserLanguage, StaticHelper.GetAutoInsertVerbiage(ImportMode.ServiceRecord, false))}: {serviceRecord.Description}",
                    Files = StaticHelper.CreateAttachmentFromRecord(ImportMode.ServiceRecord, convertedRecord.Id, convertedRecord.Description)
                });
            }
            return Json(OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage));
        }
        [HttpGet]
        public IActionResult GetAddServiceRecordPartialView(int vehicleId)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), vehicleId, HouseholdPermission.Edit)) return Forbid();
            return PartialView("Service/_ServiceRecordModal", new ServiceRecordInput()
            {
                VehicleId = vehicleId,
                ExtraFields = _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.ServiceRecord).ExtraFields,
                MaintenanceItems = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(vehicleId).Where(x => x.IsMaintenance).OrderBy(x => x.Description).ToList()
            });
        }
        [HttpGet]
        public IActionResult GetServiceRecordForEditById(int serviceRecordId)
        {
            var result = _serviceRecordDataAccess.GetServiceRecordById(serviceRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), result.VehicleId, HouseholdPermission.View))
            {
                return Forbid();
            }
            //convert to Input object.
            var convertedResult = new ServiceRecordInput
            {
                Id = result.Id,
                ServiceKey = result.ServiceKey,
                Cost = result.Cost,
                Date = result.Date.ToShortDateString(),
                Description = result.Description,
                Mileage = result.Mileage,
                Notes = result.Notes,
                VehicleId = result.VehicleId,
                Files = result.Files,
                Tags = result.Tags,
                RequisitionHistory = result.RequisitionHistory,
                ExtraFields = StaticHelper.AddExtraFields(result.ExtraFields, _extraFieldDataAccess.GetExtraFieldsById((int)ImportMode.ServiceRecord).ExtraFields),
                MaintenanceItems = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(result.VehicleId).Where(x => x.IsMaintenance).OrderBy(x => x.Description).ToList()
            };
            return PartialView("Service/_ServiceRecordModal", convertedResult);
        }
        private OperationResponse DeleteServiceRecordWithChecks(int serviceRecordId)
        {
            var existingRecord = _serviceRecordDataAccess.GetServiceRecordById(serviceRecordId);
            //security check.
            if (!_userLogic.UserCanEditVehicle(GetUserID(), existingRecord.VehicleId, HouseholdPermission.Delete))
            {
                return OperationResponse.Failed("Access Denied");
            }
            //restore any requisitioned supplies.
            if (existingRecord.RequisitionHistory.Any())
            {
                _vehicleLogic.RestoreSupplyRecordsByUsage(existingRecord.RequisitionHistory, existingRecord.Description);
            }
            var result = _serviceRecordDataAccess.DeleteServiceRecordById(existingRecord.Id);
            if (result)
            {
                _eventLogic.PublishEvent(GetUserID(), WebHookPayload.FromGenericRecord(existingRecord, "servicerecord.delete", User.Identity?.Name ?? string.Empty));
                if (!string.IsNullOrWhiteSpace(existingRecord.ServiceKey)) RecalculateMaintenanceItem(existingRecord.VehicleId, existingRecord.ServiceKey);
            }
            return OperationResponse.Conditional(result, string.Empty, StaticHelper.GenericErrorMessage);
        }

        private void RecalculateMaintenanceItem(int vehicleId, string serviceKey)
        {
            var reminder = _reminderRecordDataAccess.GetReminderRecordsByVehicleId(vehicleId)
                .FirstOrDefault(x => x.IsMaintenance && x.ServiceKey.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
            if (reminder is null) return;
            var history = _serviceRecordDataAccess.GetServiceRecordsByVehicleId(vehicleId)
                .Where(x => x.ServiceKey.Equals(serviceKey, StringComparison.OrdinalIgnoreCase)).ToList();
            MaintenanceScheduleHelper.Recalculate(reminder, history, _vehicleLogic.GetMaxMileage(vehicleId), DateTime.Today);
            _reminderRecordDataAccess.SaveReminderRecordToVehicle(reminder);
        }
        [HttpPost]
        public IActionResult DeleteServiceRecordById(int serviceRecordId)
        {
            var result = DeleteServiceRecordWithChecks(serviceRecordId);
            return Json(result);
        }
    }
}
