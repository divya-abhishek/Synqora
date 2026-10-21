using WixToolset.Data;
using WixToolset.Data.WindowsInstaller;
using WixToolset.Extensibility;

namespace Synqora.Setup.Metadata;

public sealed class Factory : BaseExtensionFactory
{
    protected override IReadOnlyCollection<Type> ExtensionTypes => new[] { typeof(RuntimeMetadata) };
}

/// <summary>Normalize only MSI language metadata rejected by the legacy File table/ICE schema.</summary>
public sealed class RuntimeMetadata : BaseWindowsInstallerBackendBinderExtension
{
    private static readonly IntermediateSymbolDefinition Marker = new("SynqoraRuntimeMetadata", Array.Empty<IntermediateFieldDefinition>(), typeof(IntermediateSymbol));
    public override void SymbolsFinalized(IntermediateSection section) => section.AddSymbol(new IntermediateSymbol(Marker));
    public override bool TryProcessSymbol(IntermediateSection section, IntermediateSymbol symbol, WindowsInstallerData data, TableDefinitionCollection definitions)
    {
        if (symbol.Definition.Name != Marker.Name) return false;
        foreach (var row in data.Tables["File"].Rows) {
            var name = row[2]?.ToString() ?? "";
            var languages = row[5]?.ToString() ?? "";
            // WinUI carries more languages than MSI's 255-character field can represent.
            // Its newer resource LCIDs also exceed the legacy ICE03 locale catalog.
            // Neutral MSI metadata keeps version-based installation; signed payload bytes,
            // satellite directories and the actual language resources stay untouched.
            if (name.Contains("Microsoft.ui.xaml", StringComparison.OrdinalIgnoreCase) &&
                (languages.Length > 255 || languages is "1152" or "1153" or "1169")) row[5] = "0";
        }
        return true;
    }
}
