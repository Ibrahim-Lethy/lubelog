using CarCareTracker.Models;

namespace CarCareTracker.Helper
{
    public static class MaintenanceScheduleHelper
    {
        public static void SetIntervals(ReminderRecord reminder, int? mileageInterval, int? monthInterval)
        {
            reminder.Metric = mileageInterval.HasValue && monthInterval.HasValue ? ReminderMetric.Both : mileageInterval.HasValue ? ReminderMetric.Odometer : ReminderMetric.Date;
            reminder.ReminderMileageInterval = mileageInterval.HasValue && Enum.IsDefined(typeof(ReminderMileageInterval), mileageInterval.Value) ? (ReminderMileageInterval)mileageInterval.Value : ReminderMileageInterval.Other;
            reminder.CustomMileageInterval = reminder.ReminderMileageInterval == ReminderMileageInterval.Other ? mileageInterval ?? 0 : 0;
            reminder.ReminderMonthInterval = monthInterval.HasValue && Enum.IsDefined(typeof(ReminderMonthInterval), monthInterval.Value) ? (ReminderMonthInterval)monthInterval.Value : ReminderMonthInterval.Other;
            reminder.CustomMonthInterval = reminder.ReminderMonthInterval == ReminderMonthInterval.Other ? monthInterval ?? 0 : 0;
            reminder.CustomMonthIntervalUnit = ReminderIntervalUnit.Months;
        }

        public static void Recalculate(ReminderRecord reminder, List<ServiceRecord> serviceRecords, int currentMileage, DateTime today)
        {
            var latest = serviceRecords.OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).FirstOrDefault();
            var latestMileage = serviceRecords.Where(x => x.Mileage > 0).OrderByDescending(x => x.Date).ThenByDescending(x => x.Mileage).FirstOrDefault();
            var needsDate = reminder.Metric is ReminderMetric.Date or ReminderMetric.Both;
            var needsMileage = reminder.Metric is ReminderMetric.Odometer or ReminderMetric.Both;
            reminder.HistoryVerified = latest is not null && (!needsMileage || latestMileage is not null);
            reminder.Date = needsDate && latest is not null ? latest.Date.AddMonths(GetMonthInterval(reminder)) : today.Date;
            reminder.Mileage = needsMileage && latestMileage is not null ? latestMileage.Mileage + GetMileageInterval(reminder) : currentMileage;
        }

        public static double CalculateProximity(ReminderRecord reminder, int currentMileage, DateTime dateCompare)
        {
            if (!reminder.HistoryVerified) return double.MaxValue;
            var progress = new List<double>();
            if (reminder.Metric is ReminderMetric.Date or ReminderMetric.Both)
            {
                var start = reminder.Date.AddMonths(-GetMonthInterval(reminder));
                progress.Add((dateCompare - start).TotalDays / Math.Max(1, (reminder.Date - start).TotalDays));
            }
            if (reminder.Metric is ReminderMetric.Odometer or ReminderMetric.Both)
            {
                var interval = GetMileageInterval(reminder);
                progress.Add(interval > 0 ? (double)(currentMileage - (reminder.Mileage - interval)) / interval : 0);
            }
            return progress.DefaultIfEmpty(0).Max();
        }

        public static bool IsDuplicateHistory(IEnumerable<ServiceRecord> records, string serviceKey, DateTime date, int mileage) =>
            records.Any(x => x.ServiceKey.Equals(serviceKey, StringComparison.OrdinalIgnoreCase) && x.Date.Date == date.Date && x.Mileage == mileage);

        public static List<ServiceRecord> FindLegacyRecords(IEnumerable<ServiceRecord> records, string name) =>
            records.Where(x => string.IsNullOrWhiteSpace(x.ServiceKey) && x.Description.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

        public static int GetMileageInterval(ReminderRecord reminder) => reminder.ReminderMileageInterval == ReminderMileageInterval.Other ? reminder.CustomMileageInterval : (int)reminder.ReminderMileageInterval;
        public static int GetMonthInterval(ReminderRecord reminder) => reminder.ReminderMonthInterval == ReminderMonthInterval.Other ? reminder.CustomMonthInterval : (int)reminder.ReminderMonthInterval;
    }
}
