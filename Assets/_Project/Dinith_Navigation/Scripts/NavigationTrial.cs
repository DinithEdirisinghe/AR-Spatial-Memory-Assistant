using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SpatialSense.RoomScan
{
    public enum NavigationTrialMode { FixedMap, LiveObstacles, FurnitureUpdates }
    public enum NavigationTrialScenario { ClearPath, AddedObstacle, RemovedObstacle, MovedFurniture }

    public struct NavigationTrialSignal
    {
        public string Name;
        public int Revision, Added, Cleared, Reopened, Restored;
        public double ConfirmationMs, ResponseMs;
    }

    [Serializable] public sealed class NavigationTrialEvent
    {
        public double seconds;
        public string kind;
        public int revision, added, cleared, reopened, restored;
        public double confirmationMs = -1, responseMs = -1;
    }

    [Serializable] public sealed class NavigationTrialRecord
    {
        public int schemaVersion = 1;
        public string trialId, sessionId, startedUtc, endedUtc, build = "v9";
        public string mode, scenario, scanMethod, roomId, itemId, itemAnchorId, outcome = "Running", reason = "";
        public bool strictCoverage, highlightsEnabled;
        public int mapGeneration, sourceRevision, semanticRevision, floorCells, furnitureCandidates;
        public double marginMetres, cellMetres, elapsedSeconds, headPathMetres, unavailableSeconds, noDepthSeconds;
        public double firstRouteSeconds = -1, initialRouteMetres = -1, finalRouteMetres = -1;
        public double addedMarkerSeconds = -1, removedMarkerSeconds = -1;
        public double addedConfirmationSeconds = -1, removedConfirmationSeconds = -1, recoveryAfterRemovalSeconds = -1;
        public double responseTotalMs, responseMaxMs;
        public int responseCount, routeChanges, blockedEvents, recoveries, falseBlocks, missedObstacles, rejectedPoseSamples, droppedEvents;
        public bool endTimeUnknown;
        public List<NavigationTrialEvent> events = new List<NavigationTrialEvent>();
    }

    // Plain data and deterministic measurements; no Unity, clock or file access.
    public sealed class NavigationTrial
    {
        public NavigationTrialRecord Record { get; }
        public bool Running => Record.outcome == "Running";
        private bool sampled, previousAvailable, previousDepth, previousPose;
        private double previousTime, previousX, previousZ;
        public NavigationTrial(NavigationTrialRecord record)
        { Record = record ?? throw new ArgumentNullException(nameof(record)); }

        public void Sample(double seconds, bool tracked, double x, double z, bool available, double routeMetres, bool depthFresh)
        {
            if (!Running || !Finite(seconds) || seconds < Record.elapsedSeconds) return;
            var delta = sampled ? seconds - previousTime : seconds;
            if (sampled && !previousAvailable) Record.unavailableSeconds += delta;
            if (sampled && !previousDepth && Record.mode != NavigationTrialMode.FixedMap.ToString()) Record.noDepthSeconds += delta;
            if (tracked && Finite(x) && Finite(z))
            {
                if (sampled && previousPose)
                {
                    var dx = x - previousX; var dz = z - previousZ; var distance = Math.Sqrt(dx * dx + dz * dz);
                    // Reject long gaps and jumps, rather than calling relocalisation walking.
                    if (delta > 0 && delta <= 1 && distance <= 3 * delta + 0.15) Record.headPathMetres += distance;
                    else if (distance > 0) Record.rejectedPoseSamples++;
                }
                previousX = x; previousZ = z; previousPose = true;
            }
            else previousPose = false;
            if (available && Finite(routeMetres) && routeMetres >= 0)
            {
                if (Record.firstRouteSeconds < 0) { Record.firstRouteSeconds = seconds; Record.initialRouteMetres = routeMetres; }
                Record.finalRouteMetres = routeMetres;
            }
            previousTime = seconds; previousAvailable = available; previousDepth = depthFresh; sampled = true;
            Record.elapsedSeconds = seconds;
        }

        public bool MarkAdded(double seconds) => Mark(seconds, true);
        public bool MarkRemoved(double seconds) => Mark(seconds, false);
        private bool Mark(double seconds, bool added)
        {
            if (!Running || !Finite(seconds) || seconds < Record.elapsedSeconds
                || (added ? Record.addedMarkerSeconds : Record.removedMarkerSeconds) >= 0) return false;
            if (added) Record.addedMarkerSeconds = seconds; else Record.removedMarkerSeconds = seconds;
            AddEvent(new NavigationTrialEvent { seconds = seconds, kind = added ? "manual_added_or_moved" : "manual_removed_or_cleared" });
            return true;
        }
        public void Flag(double seconds, bool falseBlock)
        {
            if (!Running || !Finite(seconds) || seconds < Record.elapsedSeconds) return;
            if (falseBlock) Record.falseBlocks++; else Record.missedObstacles++;
            AddEvent(new NavigationTrialEvent { seconds = seconds, kind = falseBlock ? "manual_false_block" : "manual_missed_obstacle" });
        }

        public void Observe(double seconds, NavigationTrialSignal signal)
        {
            if (!Running || !Finite(seconds) || seconds < Record.elapsedSeconds) return;
            if (signal.Name == "route_shape_changed") Record.routeChanges++;
            if (signal.Name == "route_blocked") Record.blockedEvents++;
            if (signal.Name == "route_recovered")
            {
                Record.recoveries++;
                if (Record.removedMarkerSeconds >= 0 && seconds >= Record.removedMarkerSeconds && Record.recoveryAfterRemovalSeconds < 0)
                    Record.recoveryAfterRemovalSeconds = seconds - Record.removedMarkerSeconds;
            }
            if (signal.Name == "live_obstacle_change")
            {
                var moved = Record.scenario == NavigationTrialScenario.MovedFurniture.ToString();
                if (Record.addedMarkerSeconds >= 0 && seconds >= Record.addedMarkerSeconds && Record.addedConfirmationSeconds < 0
                    && (signal.Added > 0 || signal.Restored > 0 || (moved && signal.Reopened > 0)))
                    Record.addedConfirmationSeconds = seconds - Record.addedMarkerSeconds;
                if (Record.removedMarkerSeconds >= 0 && seconds >= Record.removedMarkerSeconds && Record.removedConfirmationSeconds < 0
                    && (signal.Cleared > 0 || signal.Reopened > 0))
                    Record.removedConfirmationSeconds = seconds - Record.removedMarkerSeconds;
            }
            if ((signal.Name == "live_route_response" || signal.Name == "live_route_no_path") && Finite(signal.ResponseMs) && signal.ResponseMs >= 0)
            {
                Record.responseCount++; Record.responseTotalMs += signal.ResponseMs;
                Record.responseMaxMs = Math.Max(Record.responseMaxMs, signal.ResponseMs);
            }
            AddEvent(new NavigationTrialEvent { seconds = seconds, kind = signal.Name, revision = signal.Revision,
                added = signal.Added, cleared = signal.Cleared, reopened = signal.Reopened, restored = signal.Restored,
                confirmationMs = Finite(signal.ConfirmationMs) && signal.ConfirmationMs >= 0 ? signal.ConfirmationMs : -1,
                responseMs = Finite(signal.ResponseMs) && signal.ResponseMs >= 0 ? signal.ResponseMs : -1 });
        }

        public bool Finish(double seconds, string outcome, string reason, string utc)
        {
            if (!Running || !Finite(seconds) || seconds < Record.elapsedSeconds
                || (outcome != "Success" && outcome != "Failed" && outcome != "Aborted")) return false;
            if (sampled)
            {
                var delta = seconds - previousTime;
                if (!previousAvailable) Record.unavailableSeconds += delta;
                if (!previousDepth && Record.mode != NavigationTrialMode.FixedMap.ToString()) Record.noDepthSeconds += delta;
            }
            Record.elapsedSeconds = seconds; Record.outcome = outcome; Record.reason = reason; Record.endedUtc = utc;
            AddEvent(new NavigationTrialEvent { seconds = seconds, kind = "trial_" + outcome.ToLowerInvariant() });
            return true;
        }
        private void AddEvent(NavigationTrialEvent entry)
        { if (Record.events.Count < 5000) Record.events.Add(entry); else Record.droppedEvents++; }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    public static class NavigationTrialCsv
    {
        public static string SummaryHeader => "schema_version,trial_id,session_id,build,started_utc,ended_utc,end_time_unknown,mode,scenario,scan_method,room_id,item_id,item_anchor_id,strict_coverage,highlights_enabled,outcome,reason,map_generation,source_revision,semantic_revision,walkable_cells,furniture_candidates,cell_metres,extra_margin_metres,elapsed_seconds,head_path_metres,rejected_pose_samples,route_unavailable_seconds,no_recent_depth_seconds,first_route_seconds,initial_route_metres,last_available_route_metres,manual_added_or_moved_at_seconds,manual_removed_or_cleared_at_seconds,manual_added_to_first_confirmation_seconds,manual_removed_to_first_confirmation_seconds,manual_removed_to_route_recovery_seconds,path_changes,blocked_events,recoveries,manual_false_blocks,manual_missed_obstacles,response_count,mean_response_ms,max_response_ms,dropped_events";
        public static string EventHeader => "trial_id,mode,scenario,scan_method,seconds,event,obstacle_revision,added_live_cells,cleared_live_cells,reopened_baseline_cells,restored_baseline_cells,observed_to_confirmed_ms,change_to_route_response_ms";
        public static string Summary(NavigationTrialRecord r) => Row(new[] {
            r.schemaVersion.ToString(CultureInfo.InvariantCulture), r.trialId,r.sessionId,r.build,r.startedUtc,r.endedUtc,r.endTimeUnknown ? "true" : "false",
            r.mode,r.scenario,r.scanMethod,r.roomId,r.itemId,r.itemAnchorId,r.strictCoverage ? "true" : "false",r.highlightsEnabled ? "true" : "false",r.outcome,r.reason,I(r.mapGeneration),I(r.sourceRevision),I(r.semanticRevision),I(r.floorCells),I(r.furnitureCandidates),
            N(r.cellMetres),N(r.marginMetres),N(r.elapsedSeconds),N(r.headPathMetres),I(r.rejectedPoseSamples),N(r.unavailableSeconds),r.mode == "FixedMap" ? "" : N(r.noDepthSeconds),
            N(r.firstRouteSeconds),N(r.initialRouteMetres),N(r.finalRouteMetres),N(r.addedMarkerSeconds),N(r.removedMarkerSeconds),N(r.addedConfirmationSeconds),N(r.removedConfirmationSeconds),N(r.recoveryAfterRemovalSeconds),
            I(r.routeChanges),I(r.blockedEvents),I(r.recoveries),I(r.falseBlocks),I(r.missedObstacles),I(r.responseCount),r.responseCount > 0 ? N(r.responseTotalMs/r.responseCount) : "",r.responseCount > 0 ? N(r.responseMaxMs) : "",I(r.droppedEvents) });
        public static string Event(NavigationTrialRecord r, NavigationTrialEvent e) => Row(new[] {
            r.trialId,r.mode,r.scenario,r.scanMethod,N(e.seconds),e.kind,I(e.revision),I(e.added),I(e.cleared),I(e.reopened),I(e.restored),N(e.confirmationMs),N(e.responseMs) });
        private static string I(int x) => x.ToString(CultureInfo.InvariantCulture);
        private static string N(double x) => double.IsNaN(x) || double.IsInfinity(x) || x < 0 ? "" : x.ToString("0.######", CultureInfo.InvariantCulture);
        private static string Row(IEnumerable<string> fields) => string.Join(",", fields.Select(Escape));
        private static string Escape(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
