using Microsoft.Win32;

namespace Tubatools.Windows11Optimizer;

internal enum OptimizerTab
{
    Overview,
    Explorer,
    Privacy,
    Performance,
    Update,
    Power,
    Notifications,
    Apps,
    Tools
}

internal enum TweakKind
{
    Toggle,
    Choice
}

internal sealed record RegistryTarget(RegistryHive Hive, string Path, string Name, RegistryValueKind Kind);

/// <summary>One registry value with the value used when the switch is on and the Windows default when it is off.</summary>
internal sealed record RegistryOp(RegistryTarget Target, object? OnValue, object? OffValue);

internal sealed record TweakChoice(
    string Label,
    IReadOnlyList<RegistryOp> Ops,
    bool IsDefault = false,
    Func<CancellationToken, Task>? Apply = null);

internal sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required OptimizerTab Tab { get; init; }
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public TweakKind Kind { get; init; } = TweakKind.Toggle;
    public IReadOnlyList<RegistryOp> Ops { get; init; } = [];
    public IReadOnlyList<TweakChoice> Choices { get; init; } = [];
    public IReadOnlyList<RegistryTarget> ExtraBackupTargets { get; init; } = [];
    /// <summary>Included in "应用推荐优化".</summary>
    public bool Recommended { get; init; }
    /// <summary>The change is visible only after Explorer restarts.</summary>
    public bool NeedsExplorerRestart { get; init; }
    public bool NeedsReboot { get; init; }
    public bool Risky { get; init; }
    /// <summary>False when the Windows default cannot be known (power plan, hibernation, ...).</summary>
    public bool HasDefault { get; init; } = true;
    /// <summary>True when Windows ships the tweak already enabled, so restoring defaults applies the on value.</summary>
    public bool DefaultOn { get; init; }
    public Func<bool?>? ReadToggle { get; init; }
    public Func<bool, CancellationToken, Task>? ApplyToggle { get; init; }
    public Func<int>? ReadChoiceIndex { get; init; }
}

internal sealed record TweakAction
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string ButtonText { get; init; } = "执行";
    public bool Risky { get; init; }
    public required Func<CancellationToken, Task<string>> Run { get; init; }
}

internal sealed record AppxEntry(string PackageName, string Title, string Description);

internal sealed record WingetApp(string Id, string Title, string Description);
