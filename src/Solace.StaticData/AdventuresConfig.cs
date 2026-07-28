using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Solace.Common;

namespace Solace.StaticData;

public sealed class AdventuresConfig
{
    public const string RandomFolder = "random";

    private static readonly string[] DefaultFolders = ["common", "uncommon", "rare", "epic", "legendary", "oobe", RandomFolder];

    public readonly AdventureSpawnConfig SpawnConfig;
    public readonly ImmutableArray<StaticBuidplate> RandomBuildplates;
    private readonly Dictionary<string, ImmutableArray<AdventureBuildplate>> _buildplatesByFolder = [];

    internal AdventuresConfig(string dir)
    {
        try
        {
            SpawnConfig = LoadSpawnConfig(dir);

            HashSet<string> folders = [.. DefaultFolders];
            foreach (AdventureCrystalType crystalType in SpawnConfig.CrystalTypes)
            {
                folders.Add(crystalType.Folder);
            }

            RandomBuildplates = LoadRandomBuildplates(dir);

            foreach (string folder in folders)
            {
                List<AdventureBuildplate> buildplates = [];
                string buildplatesFile = Path.Combine(dir, folder, $"{folder}-buildplates.json");
                if (File.Exists(buildplatesFile))
                {
                    using var stream = File.OpenRead(buildplatesFile);
                    AdventureBuildplatesFile? buildplatesConfig = Json.Deserialize<AdventureBuildplatesFile>(stream);
                    Debug.Assert(buildplatesConfig is not null);
                    buildplates.AddRange(buildplatesConfig.Buildplates);
                }

                if (folder.Equals(RandomFolder, StringComparison.OrdinalIgnoreCase))
                {
                    var configuredTemplateIds = buildplates
                        .Select(buildplate => Path.GetFileNameWithoutExtension(buildplate.TemplateId))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    buildplates.AddRange(RandomBuildplates
                        .Where(buildplate => configuredTemplateIds.Add(buildplate.Id))
                        .Select(buildplate => new AdventureBuildplate(buildplate.Id, 10)));
                }

                ImmutableArray<AdventureBuildplate> normalizedBuildplates = [.. buildplates
                    .Where(buildplate => !string.IsNullOrWhiteSpace(buildplate.TemplateId))
                    .Select(buildplate => buildplate with
                    {
                        TemplateId = Path.GetFileNameWithoutExtension(buildplate.TemplateId),
                        Weight = int.Max(0, buildplate.Weight)
                    })
                    .Where(buildplate => buildplate.Weight > 0)];

                if (normalizedBuildplates.Length > 0)
                {
                    _buildplatesByFolder[folder] = normalizedBuildplates;
                }
            }
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public bool CanSpawn => SpawnConfig.CrystalTypes.Length > 0 && SpawnConfig.MaxCount > 0;

    public AdventureCrystalType? PickCrystalType(Random random)
        => PickWeighted(SpawnConfig.CrystalTypes, item => item.PickWeight, random);

    public long GetDurationForRarity(AdventureCrystalType.RarityE rarity)
    {
        long? configuredDuration = SpawnConfig.CrystalTypes
            .FirstOrDefault(crystalType => crystalType.Rarity == rarity)
            ?.DurationMs;

        return configuredDuration is > 0
            ? configuredDuration.Value
            : GetDefaultDurationForRarity(rarity);
    }

    public static long GetDefaultDurationForRarity(AdventureCrystalType.RarityE rarity)
        => rarity switch
        {
            AdventureCrystalType.RarityE.COMMON => 10 * 60 * 1000,
            AdventureCrystalType.RarityE.UNCOMMON => 15 * 60 * 1000,
            AdventureCrystalType.RarityE.RARE => 15 * 60 * 1000,
            AdventureCrystalType.RarityE.EPIC => 30 * 60 * 1000,
            AdventureCrystalType.RarityE.LEGENDARY => 60 * 60 * 1000,
            AdventureCrystalType.RarityE.OOBE => 60 * 60 * 1000,
            _ => throw new UnreachableException()
        };

    public string? PickTemplateForFolder(string folder, Random random)
    {
        int randomBuildplateChance = int.Clamp(SpawnConfig.RandomBuildplateChance, 0, 100);
        if (!folder.Equals(RandomFolder, StringComparison.OrdinalIgnoreCase) &&
            randomBuildplateChance > 0 &&
            randomBuildplateChance > random.Next(0, 100))
        {
            string? randomTemplate = PickTemplateFromFolder(RandomFolder, random);
            if (randomTemplate is not null)
            {
                return randomTemplate;
            }
        }

        return PickTemplateFromFolder(folder, random);
    }

    private string? PickTemplateFromFolder(string folder, Random random)
    {
        if (!_buildplatesByFolder.TryGetValue(folder, out ImmutableArray<AdventureBuildplate> buildplates) || buildplates.Length == 0)
        {
            return null;
        }

        return PickWeighted(buildplates, buildplate => buildplate.Weight, random)?.TemplateId;
    }

    public string? TryPickTemplateForCrystalItem(string itemName, Random random)
    {
        string normalizedName = itemName.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)
            ? itemName["minecraft:".Length..]
            : itemName;

        const string prefix = "adventure_crystal_";
        if (!normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string folder = normalizedName[prefix.Length..];
        return PickTemplateForFolder(folder, random);
    }

    private static ImmutableArray<StaticBuidplate> LoadRandomBuildplates(string dir)
    {
        string randomDir = Path.Combine(dir, RandomFolder);
        if (!Directory.Exists(randomDir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(randomDir)
            .Where(file =>
            {
                string extension = Path.GetExtension(file);
                return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
            })
            .Where(file => !Path.GetFileName(file).Equals($"{RandomFolder}-buildplates.json", StringComparison.OrdinalIgnoreCase))
            .Select(file => new StaticBuidplate(file))
            .OrderBy(buildplate => buildplate.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(buildplate => buildplate.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .DistinctBy(buildplate => buildplate.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private static AdventureSpawnConfig LoadSpawnConfig(string dir)
    {
        string spawnConfigFile = Path.Combine(dir, "adventures-spawn.json");
        if (!File.Exists(spawnConfigFile))
        {
            return AdventureSpawnConfig.Disabled;
        }

        using var stream = File.OpenRead(spawnConfigFile);
        AdventureSpawnConfig? spawnConfig = Json.Deserialize<AdventureSpawnConfig>(stream);
        Debug.Assert(spawnConfig is not null);
        return spawnConfig;
    }

    private static T? PickWeighted<T>(IReadOnlyList<T> items, Func<T, int> weightSelector, Random random)
    {
        int totalWeight = items.Sum(weightSelector);
        if (totalWeight <= 0)
        {
            return default;
        }

        int roll = random.Next(0, totalWeight);
        foreach (T item in items)
        {
            roll -= weightSelector(item);
            if (roll < 0)
            {
                return item;
            }
        }

        return items[^1];
    }

    public sealed record AdventureSpawnConfig(
        int MinCount,
        int MaxCount,
        long MinSpawnDelayMs,
        long MaxSpawnDelayMs,
        long MinDurationMs,
        long MaxDurationMs,
        int ChancePerSpawnCycle,
        AdventureCrystalType[] CrystalTypes
    )
    {
        public int RandomBuildplateChance { get; init; } = 90;

        public static AdventureSpawnConfig Disabled => new(0, 0, 0, 0, 0, 0, 0, [])
        {
            RandomBuildplateChance = 0
        };
    }

    public sealed record AdventureCrystalType(
        string Folder,
        string Icon,
        AdventureCrystalType.RarityE Rarity,
        int PickWeight
    )
    {
        public long? DurationMs { get; init; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum RarityE
        {
            COMMON,
            UNCOMMON,
            RARE,
            EPIC,
            LEGENDARY,
            OOBE
        }
    }

    private sealed record AdventureBuildplatesFile(
        AdventureBuildplate[] Buildplates
    );

    private sealed record AdventureBuildplate(
        string TemplateId,
        int Weight
    );
}
