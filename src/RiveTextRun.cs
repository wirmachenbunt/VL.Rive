namespace VL.Rive;

/// <summary>
/// A single named text-run assignment for <see cref="RiveRenderer"/>'s text-runs input.
/// This is the direct (cables.gl-style) way to drive text, independent of data binding:
/// the <see cref="Name"/> must match a text run's name in the Rive file.
/// <para>
/// Leave <see cref="Path"/> empty for a run in the main artboard - it is found by name at any
/// group/layer depth. Set <see cref="Path"/> only when the run lives inside an embedded nested
/// artboard; it is a "/"-delimited path of the nested-artboard names (e.g. "Outer/Inner").
/// </para>
/// <para>
/// IMPORTANT: the .riv must be exported from Rive with "Export with all names" enabled (Export
/// options). Otherwise text-run names are stripped from the file and lookups by name always fail.
/// </para>
/// </summary>
public readonly record struct RiveTextRun(string Name, string Value, string Path = "");
