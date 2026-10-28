using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpatialSense.Persistence;

namespace SpatialSense.RoomScan
{
    public sealed class NavigationTrialExport
    {
        public string SummaryPath, EventsPath, CompletionPath;
        public int Trials, Events, Skipped;
    }

    public sealed class NavigationTrialStore
    {
        public string Root { get; }
        public NavigationTrialStore(string root)
        { Root = Path.GetFullPath(root); Directory.CreateDirectory(Path.Combine(Root, "trials")); }

        public void Save(NavigationTrialRecord record, string json)
        {
            if (!Valid(record) || string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Invalid evaluation record.");
            AtomicAnchorRecordStore.Write(Path.Combine(Root, "trials", record.trialId + ".json"), json);
        }

        public IEnumerable<NavigationTrialRecord> Read(Func<string, NavigationTrialRecord> parse, Action<string> invalid = null)
        {
            var files = Directory.GetFiles(Path.Combine(Root, "trials"), "*.json"); Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                NavigationTrialRecord record = null;
                try
                {
                    if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Oversized record.");
                    record = parse(File.ReadAllText(file));
                    if (!Valid(record) || record.trialId != Path.GetFileNameWithoutExtension(file)) record = null;
                }
                catch (Exception) { record = null; }
                if (record == null) { invalid?.Invoke(Path.GetFileName(file)); continue; }
                yield return record;
            }
        }

        public int RecoverInterrupted(Func<string, NavigationTrialRecord> parse, Func<NavigationTrialRecord, string> serialize)
        {
            var recovered = 0;
            foreach (var record in Read(parse))
            {
                if (record.outcome != "Running") continue;
                record.outcome = "Aborted"; record.reason = "interrupted_before_final_save";
                record.endTimeUnknown = true; record.endedUtc = "";
                Save(record, serialize(record)); recovered++;
            }
            return recovered;
        }

        public NavigationTrialExport Export(Func<string, NavigationTrialRecord> parse)
        {
            var folder = Path.Combine(Root, "exports"); Directory.CreateDirectory(folder);
            var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var result = new NavigationTrialExport { SummaryPath = Path.Combine(folder, "summary-" + id + ".csv"),
                EventsPath = Path.Combine(folder, "events-" + id + ".csv"), CompletionPath = Path.Combine(folder, "complete-" + id + ".txt") };
            var summaryTemp = result.SummaryPath + ".tmp"; var eventTemp = result.EventsPath + ".tmp";
            var issues = new List<string>();
            try
            {
                using (var summary = new StreamWriter(summaryTemp, false, new UTF8Encoding(true)))
                using (var events = new StreamWriter(eventTemp, false, new UTF8Encoding(true)))
                {
                    summary.WriteLine(NavigationTrialCsv.SummaryHeader); events.WriteLine(NavigationTrialCsv.EventHeader);
                    foreach (var record in Read(parse, file => { result.Skipped++; issues.Add(file); }))
                    {
                        if (record.outcome == "Running") { result.Skipped++; issues.Add(record.trialId + ": still running"); continue; }
                        summary.WriteLine(NavigationTrialCsv.Summary(record)); result.Trials++;
                        foreach (var entry in record.events)
                        { if (entry == null) continue; events.WriteLine(NavigationTrialCsv.Event(record, entry)); result.Events++; }
                    }
                }
                File.Move(summaryTemp, result.SummaryPath); File.Move(eventTemp, result.EventsPath);
                AtomicAnchorRecordStore.Write(result.CompletionPath, "Completed evaluation export\nSummary: " + Path.GetFileName(result.SummaryPath)
                    + "\nEvents: " + Path.GetFileName(result.EventsPath) + "\nTrials: " + result.Trials + "\nEvent rows: " + result.Events
                    + "\nSkipped: " + result.Skipped + "\n" + string.Join("\n", issues));
                return result;
            }
            finally
            {
                if (File.Exists(summaryTemp)) File.Delete(summaryTemp);
                if (File.Exists(eventTemp)) File.Delete(eventTemp);
            }
        }

        private static bool Valid(NavigationTrialRecord r)
        {
            return r != null && r.schemaVersion == 1 && Guid.TryParseExact(r.trialId, "N", out _)
                && Guid.TryParseExact(r.sessionId, "N", out _) && DateTimeOffset.TryParse(r.startedUtc, out _)
                && Enum.TryParse<NavigationTrialMode>(r.mode, out var mode) && Enum.IsDefined(typeof(NavigationTrialMode), mode)
                && Enum.TryParse<NavigationTrialScenario>(r.scenario, out var scenario) && Enum.IsDefined(typeof(NavigationTrialScenario), scenario)
                && r.mode == mode.ToString() && r.scenario == scenario.ToString()
                && (r.outcome == "Running" || r.outcome == "Success" || r.outcome == "Failed" || r.outcome == "Aborted")
                && (r.outcome == "Running" || (r.outcome == "Aborted" && r.endTimeUnknown) || DateTimeOffset.TryParse(r.endedUtc, out _))
                && !double.IsNaN(r.elapsedSeconds) && !double.IsInfinity(r.elapsedSeconds) && r.elapsedSeconds >= 0
                && r.events != null && r.events.Count <= 5000;
        }
    }
}
