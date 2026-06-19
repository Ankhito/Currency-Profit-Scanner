using System.Reflection;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CurrencyProfitScanner;

public sealed class LuminaDiscoveryService
{
    private static readonly Type[] CompileVisibleSheetTypes =
    [
        typeof(Item),
        typeof(SpecialShop),
        typeof(Fate),
        typeof(FittingShopCategoryItem),
        typeof(WKSAchievement),
        typeof(WKSDevGrade),
        typeof(WKSEmergencyInfo),
        typeof(WKSEmergencyMission),
        typeof(WKSEmergencyProblem),
        typeof(WKSFunction),
        typeof(WKSMissionReward),
        typeof(WKSPioneeringTrail),
    ];

    private readonly IDataManager dataManager;

    public LuminaDiscoveryService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<LuminaSheetDiscovery> Discover()
    {
        return CompileVisibleSheetTypes
            .Select(type => new LuminaSheetDiscovery(
                type.Name,
                this.CanLoadSheet(type),
                DescribeProperties(type),
                this.AssessCandidateUse(type)))
            .OrderBy(report => report.SheetClassName, StringComparer.Ordinal)
            .ToList();
    }

    private bool CanLoadSheet(Type sheetType)
    {
        var method = typeof(IDataManager).GetMethods()
            .FirstOrDefault(method => method.Name == nameof(IDataManager.GetExcelSheet) && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
        if (method is null)
        {
            return false;
        }

        try
        {
            var sheet = method.MakeGenericMethod(sheetType).Invoke(this.dataManager, null);
            return sheet is not null;
        }
        catch
        {
            return false;
        }
    }

    private LuminaCandidateAssessment AssessCandidateUse(Type sheetType)
    {
        var properties = sheetType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var names = properties.Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sheetType == typeof(Item))
        {
            return new LuminaCandidateAssessment(
                ResultItem: HasAny(names, "RowId"),
                CostCurrency: false,
                CostAmount: false,
                QuantityReceived: false,
                SourceShopName: false,
                Notes: "Item can identify item metadata only; marketability depends on available properties.");
        }

        if (sheetType == typeof(SpecialShop))
        {
            return new LuminaCandidateAssessment(
                ResultItem: true,
                CostCurrency: true,
                CostAmount: true,
                QuantityReceived: true,
                SourceShopName: HasAny(names, "Name"),
                Notes: "SpecialShop.ItemStruct.ReceiveItemsStruct has Item and ReceiveCount; ItemCostsStruct has ItemCost, CurrencyCost, CostType, and CollectabilityCost.");
        }

        return new LuminaCandidateAssessment(false, false, false, false, false, "Not assessed as a direct currency shop source.");
    }

    private static bool HasAny(IReadOnlySet<string> names, params string[] candidates) => candidates.Any(names.Contains);

    private static IReadOnlyList<string> DescribeProperties(Type sheetType)
    {
        var lines = new List<string>();
        AddProperties(lines, sheetType, sheetType.Name);
        foreach (var nestedType in sheetType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            AddProperties(lines, nestedType, $"{sheetType.Name}.{nestedType.Name}");
            foreach (var secondLevel in nestedType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).OrderBy(type => type.Name, StringComparer.Ordinal))
            {
                AddProperties(lines, secondLevel, $"{sheetType.Name}.{nestedType.Name}.{secondLevel.Name}");
            }
        }

        return lines;
    }

    private static void AddProperties(List<string> lines, Type type, string label)
    {
        lines.AddRange(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{label}.{property.Name}: {FriendlyName(property.PropertyType)}"));
    }

    private static string FriendlyName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var genericName = type.Name.Split('`')[0];
        return $"{genericName}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
    }
}

public sealed record LuminaSheetDiscovery(
    string SheetClassName,
    bool CanLoadSheet,
    IReadOnlyList<string> PublicProperties,
    LuminaCandidateAssessment CandidateAssessment);

public sealed record LuminaCandidateAssessment(
    bool ResultItem,
    bool CostCurrency,
    bool CostAmount,
    bool QuantityReceived,
    bool SourceShopName,
    string Notes);
