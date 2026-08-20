using CarCareTracker.Helper;
using CarCareTracker.Models;
using System.Text.Json;
using Xunit;

namespace CarCareTracker.Tests;

public class MaintenanceTests
{
    [Fact]
    public void CsvAndJsonSchedulesProduceTheSameRecord()
    {
        const string csv = "service_key,name,mileage_interval,month_interval,notes\nengine-oil,Engine Oil,8000,6,Synthetic\n";
        const string json = "[{\"service_key\":\"engine-oil\",\"name\":\"Engine Oil\",\"mileage_interval\":8000,\"month_interval\":6,\"notes\":\"Synthetic\"}]";

        var fromCsv = MaintenanceImportHelper.ParseSchedule(csv);
        var fromJson = MaintenanceImportHelper.ParseSchedule(json);

        Assert.All(fromCsv.Rows.Concat(fromJson.Rows), row => Assert.False(row.IsError));
        Assert.Equal(JsonSerializer.Serialize(fromCsv.Records), JsonSerializer.Serialize(fromJson.Records));
    }

    [Fact]
    public void CsvAndJsonHistoryProduceTheSameRecord()
    {
        const string csv = "service_key,date,odometer,cost,notes\nengine-oil,2026-01-15,45000,89.99,CARFAX\n";
        const string json = "[{\"service_key\":\"engine-oil\",\"date\":\"2026-01-15\",\"odometer\":45000,\"cost\":89.99,\"notes\":\"CARFAX\"}]";

        var fromCsv = MaintenanceImportHelper.ParseHistory(csv);
        var fromJson = MaintenanceImportHelper.ParseHistory(json);

        Assert.All(fromCsv.Rows.Concat(fromJson.Rows), row => Assert.False(row.IsError));
        Assert.Equal(JsonSerializer.Serialize(fromCsv.Records), JsonSerializer.Serialize(fromJson.Records));
    }

    [Theory]
    [InlineData("Bad Key", "Oil", 5000, null)]
    [InlineData("oil", "Oil", null, null)]
    [InlineData("oil", "", 5000, null)]
    [InlineData("oil", "Oil", -1, null)]
    public void ScheduleValidationRejectsUnsafeRows(string key, string name, int? mileage, int? months)
    {
        var json = JsonSerializer.Serialize(new[] { new MaintenanceScheduleImport { ServiceKey = key, Name = name, MileageInterval = mileage, MonthInterval = months } });
        Assert.Contains(MaintenanceImportHelper.ParseSchedule(json).Rows, row => row.IsError);
    }

    [Fact]
    public void HistoryValidationRequiresIsoDateAndNonNegativeValues()
    {
        const string json = "[{\"service_key\":\"engine-oil\",\"date\":\"01/15/2026\",\"odometer\":-1,\"cost\":-2}]";
        var result = MaintenanceImportHelper.ParseHistory(json);
        Assert.True(result.Rows.Single().IsError);
        Assert.Contains("YYYY-MM-DD", result.Rows.Single().Message);
    }

    [Fact]
    public void ImportValidationRejectsDuplicateKeysSizeAndRowLimits()
    {
        const string duplicates = "service_key,name,mileage_interval,month_interval,notes\nengine-oil,Oil,5000,,\nENGINE-OIL,Oil,5000,,\n";
        Assert.Contains(MaintenanceImportHelper.ParseSchedule(duplicates).Rows, row => row.Message.Contains("duplicated"));

        var oversized = new string('x', MaintenanceImportHelper.MaxBytes + 1);
        Assert.Contains("1 MB", MaintenanceImportHelper.ParseSchedule(oversized).Rows.Single().Message);

        var tooManyRows = Enumerable.Range(0, MaintenanceImportHelper.MaxRows + 1)
            .Select(index => new MaintenanceScheduleImport { ServiceKey = $"service-{index}", Name = $"Service {index}", MileageInterval = 5000 });
        var tooMany = MaintenanceImportHelper.ParseSchedule(JsonSerializer.Serialize(tooManyRows));
        Assert.Contains($"{MaintenanceImportHelper.MaxRows:N0}", tooMany.Rows.Single().Message);
    }

    [Fact]
    public void MissingHistoryIsDueNowAndUnverified()
    {
        var reminder = NewCombinedReminder();
        var today = new DateTime(2026, 8, 19);
        MaintenanceScheduleHelper.Recalculate(reminder, [], 72000, today);
        Assert.False(reminder.HistoryVerified);
        Assert.Equal(today, reminder.Date);
        Assert.Equal(72000, reminder.Mileage);
    }

    [Fact]
    public void HistoryOrderDoesNotChangeNextDueCalculation()
    {
        var history = new List<ServiceRecord> { new() { ServiceKey = "engine-oil", Date = new DateTime(2026, 2, 1), Mileage = 68000 } };
        var scheduleFirst = NewCombinedReminder();
        var historyFirst = NewCombinedReminder();
        MaintenanceScheduleHelper.Recalculate(scheduleFirst, history, 70000, new DateTime(2026, 8, 19));
        MaintenanceScheduleHelper.Recalculate(historyFirst, history, 70000, new DateTime(2026, 8, 19));
        Assert.Equal(scheduleFirst.Date, historyFirst.Date);
        Assert.Equal(scheduleFirst.Mileage, historyFirst.Mileage);
        Assert.Equal(new DateTime(2026, 8, 1), scheduleFirst.Date);
        Assert.Equal(76000, scheduleFirst.Mileage);
    }

    [Fact]
    public void DateMileageAndCombinedIntervalsUseTheirLatestValidAnchors()
    {
        var records = new List<ServiceRecord>
        {
            new() { ServiceKey = "engine-oil", Date = new DateTime(2026, 1, 1), Mileage = 68000 },
            new() { ServiceKey = "engine-oil", Date = new DateTime(2026, 2, 1), Mileage = 0 }
        };
        var dateOnly = new ReminderRecord();
        MaintenanceScheduleHelper.SetIntervals(dateOnly, null, 6);
        MaintenanceScheduleHelper.Recalculate(dateOnly, records, 70000, new DateTime(2026, 8, 19));
        Assert.Equal(new DateTime(2026, 8, 1), dateOnly.Date);
        Assert.True(dateOnly.HistoryVerified);

        var mileageOnly = new ReminderRecord();
        MaintenanceScheduleHelper.SetIntervals(mileageOnly, 8000, null);
        MaintenanceScheduleHelper.Recalculate(mileageOnly, records, 70000, new DateTime(2026, 8, 19));
        Assert.Equal(76000, mileageOnly.Mileage);
        Assert.True(mileageOnly.HistoryVerified);

        var combined = NewCombinedReminder();
        MaintenanceScheduleHelper.Recalculate(combined, records, 70000, new DateTime(2026, 8, 19));
        Assert.Equal(new DateTime(2026, 8, 1), combined.Date);
        Assert.Equal(76000, combined.Mileage);
    }

    [Fact]
    public void ClosestOfTimeOrMileageControlsProximity()
    {
        var mileageCloser = NewCombinedReminder();
        mileageCloser.HistoryVerified = true;
        mileageCloser.Date = new DateTime(2026, 12, 1);
        mileageCloser.Mileage = 71000;
        var farAway = NewCombinedReminder();
        farAway.HistoryVerified = true;
        farAway.Date = new DateTime(2027, 1, 1);
        farAway.Mileage = 77000;

        Assert.True(MaintenanceScheduleHelper.CalculateProximity(mileageCloser, 70000, new DateTime(2026, 8, 19)) >
                    MaintenanceScheduleHelper.CalculateProximity(farAway, 70000, new DateTime(2026, 8, 19)));
    }

    [Fact]
    public void DuplicateAndLegacyMatchingAreDeterministic()
    {
        var records = new List<ServiceRecord>
        {
            new() { ServiceKey = "engine-oil", Date = new DateTime(2026, 1, 1), Mileage = 50000 },
            new() { Description = "Engine Oil", Date = new DateTime(2025, 1, 1), Mileage = 40000 }
        };
        Assert.True(MaintenanceScheduleHelper.IsDuplicateHistory(records, "ENGINE-OIL", new DateTime(2026, 1, 1), 50000));
        Assert.Single(MaintenanceScheduleHelper.FindLegacyRecords(records, "engine oil"));
    }

    [Fact]
    public void NewFieldsRoundTripThroughJsonStorage()
    {
        var reminder = NewCombinedReminder();
        reminder.ServiceKey = "engine-oil";
        reminder.IsMaintenance = true;
        var service = new ServiceRecord { ServiceKey = "engine-oil" };
        Assert.Equal("engine-oil", JsonSerializer.Deserialize<ReminderRecord>(JsonSerializer.Serialize(reminder))!.ServiceKey);
        Assert.Equal("engine-oil", JsonSerializer.Deserialize<ServiceRecord>(JsonSerializer.Serialize(service))!.ServiceKey);
    }

    private static ReminderRecord NewCombinedReminder()
    {
        var reminder = new ReminderRecord { IsMaintenance = true, ServiceKey = "engine-oil" };
        MaintenanceScheduleHelper.SetIntervals(reminder, 8000, 6);
        return reminder;
    }
}
