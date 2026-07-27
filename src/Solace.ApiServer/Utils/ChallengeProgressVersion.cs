namespace Solace.ApiServer.Utils;

public sealed class ChallengeProgressVersion
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }
    public long UpdatedAt { get; set; }
    public string? DailyDateUtc { get; set; }
    public string? ActiveSeasonId { get; set; }
    public string? ActiveSeasonChallengeId { get; set; }
    public int TappablesRedeemed { get; set; }
    public Dictionary<string, int> ObjectiveCounts { get; set; } = [];
    public Dictionary<string, int> DailyObjectiveCounts { get; set; } = [];
    public HashSet<string> ClaimedChallengeIds { get; set; } = [];
    public HashSet<string> DailyClaimedChallengeIds { get; set; } = [];
    public HashSet<string> RemovedContinuousChallengeIds { get; set; } = [];

    public void EnsureDate(long timestamp)
    {
        string today = DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
            .UtcDateTime
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        ObjectiveCounts ??= [];
        DailyObjectiveCounts ??= [];
        ClaimedChallengeIds ??= [];
        DailyClaimedChallengeIds ??= [];
        RemovedContinuousChallengeIds ??= [];

        bool dateChanged = DailyDateUtc != today;

        if (SchemaVersion < CurrentSchemaVersion)
        {
            DailyObjectiveCounts = dateChanged ? [] : new Dictionary<string, int>(ObjectiveCounts);
            DailyClaimedChallengeIds = [];
            SchemaVersion = CurrentSchemaVersion;
        }

        if (DailyDateUtc == today)
        {
            return;
        }

        DailyDateUtc = today;
        DailyObjectiveCounts = [];
        DailyClaimedChallengeIds = [];
    }

    public int RecordTappable(long timestamp)
    {
        EnsureDate(timestamp);
        UpdatedAt = timestamp;
        TappablesRedeemed++;
        return TappablesRedeemed;
    }

    public void AddObjectiveProgress(long timestamp, string objectiveId, int amount = 1)
    {
        EnsureDate(timestamp);
        UpdatedAt = timestamp;
        ObjectiveCounts ??= [];
        ObjectiveCounts[objectiveId] = ObjectiveCounts.GetValueOrDefault(objectiveId) + amount;
    }

    public void AddDailyObjectiveProgress(long timestamp, string objectiveId, int amount = 1)
    {
        EnsureDate(timestamp);
        UpdatedAt = timestamp;
        DailyObjectiveCounts ??= [];
        DailyObjectiveCounts[objectiveId] = DailyObjectiveCounts.GetValueOrDefault(objectiveId) + amount;
    }

    public int GetObjectiveProgress(string objectiveId)
    {
        ObjectiveCounts ??= [];
        return ObjectiveCounts.GetValueOrDefault(objectiveId);
    }

    public int GetDailyObjectiveProgress(string objectiveId)
    {
        DailyObjectiveCounts ??= [];
        return DailyObjectiveCounts.GetValueOrDefault(objectiveId);
    }
}
