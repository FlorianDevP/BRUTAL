using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ILSpyX = ICSharpCode.ILSpyX;
using Brutal.Logging;
using System.Reflection;
using System.Collections.Immutable;

namespace glfw_opengl;

public sealed record Namespace(string Name, List<Type> Types);

public sealed class Type(ITypeDefinition definition)
{
    public ITypeDefinition Definition { get; } = definition;
    public SymbolKind Kind => Definition.SymbolKind;
    public DecompiledType? Decompiled;
}

public sealed record DecompiledType(string Source, ImmutableArray<Exception>? Errors);

public class CompilationAnalysis
{
    /// <summary>
    /// The logging category for all CompilationAnalysis-related logs.
    /// </summary>
    private static readonly LogCategory Log = LogCategory.Make("CompilationAnalysis");

    public static readonly PropertyInfo[] decompilerProperties = [.. from p in typeof(DecompilerSettings).GetProperties() where p.GetCustomAttribute<System.ComponentModel.BrowsableAttribute>()?.Browsable ?? true select p];

    private readonly string ILSpySettingsFile = "C:\\Users\\flori\\AppData\\Roaming\\ICSharpCode\\ILSpy.xml";

    public readonly ILSpyX.Settings.DecompilerSettings decompilerSettings;

    private readonly CSharpDecompiler Decompiler;
    public readonly List<Namespace> Namespaces;

    public CompilationAnalysis()
    {
        //         var properties = (from p in typeof(ICSharpCode.Decompiler.DecompilerSettings).GetProperties() where p.GetCustomAttribute<BrowsableAttribute>()?.Browsable ?? true select p).ToArray();
        //         properties.First().GetValue(settings);

        // get decompile settings
        if (ILSpySettingsFile != null)
        {
            try
            {
                ILSpyX.Settings.ILSpySettings.SettingsFilePathProvider = () => ILSpySettingsFile;
                var settingsService = new ILSpyX.Settings.SettingsServiceBase(ILSpyX.Settings.ILSpySettings.Load());
                decompilerSettings = settingsService.GetSettings<ILSpyX.Settings.DecompilerSettings>();
            }
            catch (Exception error)
            {
                Log.Error($"Error loading ILSpy settings file '{ILSpySettingsFile}': {error.Message}");
            }
        }
        decompilerSettings ??= new ILSpyX.Settings.DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = false,
        };

        // create decompiler
        Decompiler = GetDecompiler(Assembly.GetExecutingAssembly());

        // get namespaces and their types
        Namespaces = new(Decompiler.TypeSystem.RootNamespace.ChildNamespaces.Count());
        foreach (var ns in Decompiler.TypeSystem.RootNamespace.ChildNamespaces)
        {
            var types = new List<Type>(ns.Types.Count());
            foreach (var type in ns.Types)
                types.Add(new Type(type));
            Namespaces.Add(new Namespace(ns.FullName, types));
        }
    }

    public void SaveSetting()
    {
        var settings = ILSpyX.Settings.ILSpySettings.Load();
        settings.SaveSettings(decompilerSettings.SaveToXml());
    }

    public void DecompileType(ref Type type)
    {
        if (type.Decompiled is not null)
            return;

        ImmutableArray<Exception>? errors = null;
        string? source = null;
        try
        {
            source = Decompiler.DecompileTypeAsString(type.Definition.FullTypeName);
        }
        catch (Exception error)
        {
            errors = [error];
        }

        if (0 < Decompiler.Errors.Count)
        {
            var assemblyName = type.Definition.ParentModule?.FullAssemblyName ?? Assembly.GetExecutingAssembly().Location;
            var errorText = new System.Text.StringBuilder($"While decompiling {assemblyName}:\n");
            errorText.AppendJoin('\n', CSharpDecompiler.GetErrorSummaryLines(Decompiler.Errors.Count));
            errorText.AppendLine();
            errorText.AppendJoin('\n', Decompiler.Errors.Select(CSharpDecompiler.GetErrorHeadline));
            var error = errorText.ToString();
            Log.Error(error);

            if (errors is null)
                errors = [new Exception(error),.. Decompiler.Errors];
            else
                errors = [..errors, new Exception(error), .. Decompiler.Errors];
        }

        // type.SymbolKind;
        type.Decompiled = new DecompiledType(source ?? string.Empty, errors);
    }

    // public string[] ReferencePaths { get; } = [];
    CSharpDecompiler GetDecompiler(Assembly assembly)
    {
        var assemblyFileName = assembly.Location;
        var entryName = assembly.EntryPoint?.Name ?? string.Empty;
        var module = ICSharpCode.ILSpyCmd.InputFileLoader.Load(assemblyFileName, entryName);
        var resolver = new UniversalAssemblyResolver(assemblyFileName, false, module.Metadata.DetectTargetFrameworkId());
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyFileName));
        // foreach (var path in (ReferencePaths ?? Array.Empty<string>()))
        // {
        //     resolver.AddSearchDirectory(path);
        // }

        if (!decompilerSettings.ApplyWindowsRuntimeProjections)
        {
            // Whether the projections are wanted is only known once the settings have been
            // read, which needs the module: load it again to get the metadata as stored.
            module = ICSharpCode.ILSpyCmd.InputFileLoader.Load(assemblyFileName, entryName, applyWinRTProjections: false);
        }
        return new CSharpDecompiler(module, resolver, decompilerSettings)
        {
            DebugInfoProvider = TryLoadPDB(module, assembly)
        };
    }

    static IDebugInfoProvider? TryLoadPDB(PEFile module, Assembly assembly)
    {
        var pdbFileName = Path.ChangeExtension(Path.GetFileNameWithoutExtension(assembly.Location), "pdb");
        var pdbFilePath = Path.Join(Path.GetDirectoryName(assembly.Location), pdbFileName);
        Log.Debug(pdbFilePath);

        if (Path.Exists(pdbFilePath))
            return ILSpyX.PdbProvider.DebugInfoUtils.FromFile(module, pdbFilePath);
        else
            return ILSpyX.PdbProvider.DebugInfoUtils.LoadSymbols(module);
    }

    private static Exception? ReportDecompilationErrors(string assemblyFileName, IReadOnlyList<DecompilerException> errors)
    {
        if (errors.Count == 0)
            return null;

        var errorText = new System.Text.StringBuilder($"While decompiling {assemblyFileName}:\n");
        errorText.AppendJoin('\n', CSharpDecompiler.GetErrorSummaryLines(errors.Count));
        errorText.AppendLine();
        errorText.AppendJoin('\n', errors.Select(CSharpDecompiler.GetErrorHeadline));
        var error = errorText.ToString();
        Log.Error(error);
        return new Exception(error);
    }
}
