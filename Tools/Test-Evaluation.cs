using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpatialSense.RoomScan;

public static class EvaluationTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected, string message) => Assert(Math.Abs(actual-expected) < 0.00001, message + $" ({actual} vs {expected})");
    private static NavigationTrialRecord Record(string mode = "FurnitureUpdates") => new NavigationTrialRecord {
        trialId = Guid.NewGuid().ToString("N"), sessionId = Guid.NewGuid().ToString("N"), startedUtc = DateTime.UtcNow.ToString("O"),
        mode = mode, scenario = "MovedFurniture", scanMethod = "Meta room scan", roomId = Guid.NewGuid().ToString("D"), itemId = "keys",
        marginMetres = 0.03, cellMetres = 0.075 };
    private static NavigationTrialSignal Signal(string name, int added=0, int cleared=0, int reopened=0, double response=-1) =>
        new NavigationTrialSignal { Name = name, Added = added, Cleared = cleared, Reopened = reopened, ResponseMs = response, ConfirmationMs = -1 };
    private static List<string> Csv(string line)
    {
        var fields = new List<string>(); var value = new System.Text.StringBuilder(); var quoted = false;
        for (var i=0;i<line.Length;i++) {
            var c=line[i];
            if (c=='"') { if (quoted && i+1<line.Length && line[i+1]=='"') { value.Append('"'); i++; } else quoted=!quoted; }
            else if(c==',' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        Assert(!quoted, "Unterminated CSV quotes."); fields.Add(value.ToString()); return fields;
    }
    public static int Main(string[] args)
    {
        try {
            var record=Record(); var trial=new NavigationTrial(record);
            trial.Sample(0,true,0,0,false,-1,false);
            trial.Sample(1,true,1,0,true,8,true);
            trial.Sample(2,true,2,0,false,-1,true);
            trial.Sample(3,true,100,0,true,4,true);
            Near(record.unavailableSeconds,2,"Route unavailable integration");
            Near(record.noDepthSeconds,1,"Depth-gap integration");
            Near(record.headPathMetres,2,"Head-path proxy");
            Assert(record.rejectedPoseSamples==1,"Pose jump was counted as walking.");
            Near(record.firstRouteSeconds,1,"First route time"); Near(record.initialRouteMetres,8,"Initial route distance");
            trial.Sample(2,true,0,0,true,0,true); Near(record.elapsedSeconds,3,"Stale sample mutated timing");
            Assert(trial.MarkAdded(3.1),"First added marker failed."); Assert(!trial.MarkAdded(3.2),"Duplicate added marker replaced the reference.");
            trial.Observe(3.3,Signal("live_obstacle_change",reopened:1));
            Near(record.addedConfirmationSeconds,0.2,"Move-to-confirmation delay");
            Assert(trial.MarkRemoved(3.5),"Removal marker failed.");
            trial.Observe(3.8,Signal("live_obstacle_change",cleared:2));
            trial.Observe(3.9,Signal("route_recovered"));
            Near(record.removedConfirmationSeconds,0.3,"Removal-to-confirmation delay"); Near(record.recoveryAfterRemovalSeconds,0.4,"Removal-to-recovery delay");
            trial.Observe(3.9,Signal("route_shape_changed")); trial.Observe(3.9,Signal("route_blocked"));
            trial.Observe(3.9,Signal("live_route_response",response:10)); trial.Observe(3.9,Signal("live_route_no_path",response:30));
            trial.Observe(3.9,Signal("live_route_response",response:double.NaN));
            Assert(record.responseCount==2 && record.routeChanges==1 && record.blockedEvents==1 && record.recoveries==1,"Navigation event counters are wrong.");
            trial.Flag(3.9,true); trial.Flag(3.9,false);
            Assert(record.falseBlocks==1 && record.missedObstacles==1,"Manual error reports were not kept separately.");
            Assert(trial.Finish(4,"Success","user_confirmed",DateTime.UtcNow.ToString("O")),"Finish failed.");
            Assert(!trial.Finish(5,"Failed","duplicate",DateTime.UtcNow.ToString("O")),"Finished trial changed outcome.");
            trial.Flag(6,true); Assert(record.falseBlocks==1,"Finished trial accepted another manual report.");

            var fixedTrial=new NavigationTrial(Record("FixedMap"));
            fixedTrial.Sample(0,true,0,0,false,-1,false); fixedTrial.Sample(2,false,0,0,false,-1,false);
            fixedTrial.Finish(3,"Failed","no_path",DateTime.UtcNow.ToString("O"));
            Near(fixedTrial.Record.unavailableSeconds,3,"Unavailable tail integration"); Near(fixedTrial.Record.noDepthSeconds,0,"Fixed mode got a false sensor outage.");
            var missing=new NavigationTrial(Record()); missing.Sample(0,false,0,0,false,-1,false);
            Assert(missing.Finish(1,"Aborted","tracking_lost",DateTime.UtcNow.ToString("O")),"Abort failed.");
            Assert(missing.Record.firstRouteSeconds==-1,"No-route trial fabricated a first route.");

            CultureInfo.CurrentCulture=new CultureInfo("de-DE");
            record.reason="=HYPERLINK(\"example\",\"comma, quote\")\r\nUnicode: රූම්";
            var headers=Csv(NavigationTrialCsv.SummaryHeader); var fields=Csv(NavigationTrialCsv.Summary(record));
            Assert(headers.Count==fields.Count,"CSV header/summary column counts differ.");
            Assert(fields[headers.IndexOf("reason")].StartsWith("'=HYPERLINK"),"Spreadsheet formula text was not neutralised.");
            Assert(fields[headers.IndexOf("reason")].Contains("\r\nUnicode: රූම්"),"CSV quotes/newlines/Unicode were corrupted.");
            Assert(fields[headers.IndexOf("extra_margin_metres")]=="0.03","Locale changed the CSV decimal separator.");
            Assert(fields[headers.IndexOf("mean_response_ms")]=="20","Mean response calculation is wrong.");
            Assert(Csv(NavigationTrialCsv.Summary(missing.Record))[headers.IndexOf("first_route_seconds")]=="","Missing route time exported as zero.");
            Assert(Csv(NavigationTrialCsv.Summary(fixedTrial.Record))[headers.IndexOf("no_recent_depth_seconds")]=="","Fixed-mode depth metric was not marked unavailable.");
            Assert(Csv(NavigationTrialCsv.EventHeader).Count==Csv(NavigationTrialCsv.Event(record,record.events[0])).Count,"Event CSV column counts differ.");
            var capped=new NavigationTrial(Record());
            for(var i=0;i<5010;i++) capped.Observe(0,Signal("test_event"));
            Assert(capped.Record.events.Count==5000 && capped.Record.droppedEvents==10,"Event bound silently lost accounting.");

            var directory=Path.Combine(Path.GetFullPath(args[0]),Guid.NewGuid().ToString("N"));
            var store=new NavigationTrialStore(directory);
            var options=new JsonSerializerOptions { IncludeFields=true };
            string Serialize(NavigationTrialRecord r) => JsonSerializer.Serialize(r,options);
            NavigationTrialRecord Parse(string text) => JsonSerializer.Deserialize<NavigationTrialRecord>(text,options);
            store.Save(record,Serialize(record));
            record.falseBlocks=2; store.Save(record,Serialize(record));
            Assert(store.Read(Parse).Single().falseBlocks==2,"Atomic checkpoint replacement did not save the current trial.");
            var interrupted=Record(); interrupted.elapsedSeconds=7; store.Save(interrupted,Serialize(interrupted));
            Assert(store.RecoverInterrupted(Parse,Serialize)==1,"Interrupted checkpoint was not recovered.");
            var recovered=store.Read(Parse).Single(r=>r.trialId==interrupted.trialId);
            Assert(recovered.outcome=="Aborted" && recovered.endTimeUnknown && recovered.endedUtc=="" && recovered.elapsedSeconds==7,"Recovery invented an exact end time or successful outcome.");
            var corrupt=Path.Combine(directory,"trials",Guid.NewGuid().ToString("N")+".json"); File.WriteAllText(corrupt,"broken json");
            var result=store.Export(Parse);
            Assert(result.Trials==2 && result.Skipped==1,"Export silently dropped or fabricated trials.");
            Assert(File.Exists(result.CompletionPath) && File.Exists(result.EventsPath),"Export completion record is missing.");
            Assert(File.Exists(corrupt),"Invalid source record was deleted.");
            Assert(!Directory.GetFiles(Path.Combine(directory,"exports"),"*.tmp").Any(),"Export left temporary output behind.");
            Assert(File.ReadAllBytes(result.SummaryPath).Take(3).SequenceEqual(new byte[]{239,187,191}),"CSV lacks its UTF-8 BOM for Excel.");
            var traversal=Record(); traversal.trialId="../escape"; var refused=false;
            try { store.Save(traversal,Serialize(traversal)); } catch(InvalidDataException) { refused=true; }
            Assert(refused,"Record ID permitted a path escape.");
            Console.WriteLine("PASS: trial timing, route/head-path metrics, pose rejection, manual markers, outcomes, missing values, bounded events, locale/Unicode/formula-safe CSV, atomic checkpoints, interrupted recovery and export integrity.");
            Console.WriteLine("Test artifacts: "+directory);
            return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
