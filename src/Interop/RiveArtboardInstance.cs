using static VL.Rive.Interop.Methods;

namespace VL.Rive.Interop;

internal class RiveArtboardInstance : RiveObject
{
    public RiveArtboardInstance(nint handle) : base(handle) { }

    public RiveAABB Bounds
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return rive_Artboard_Bounds(handle);
        }
    }

    public unsafe RiveScene? GetDefaultScene()
    {
        bool isAnimation = false;
        var sceneHandle = rive_ArtboardInstance_DefaultScene(handle, &isAnimation);
        if (sceneHandle == nint.Zero)
            return null;
        return new RiveScene(sceneHandle) { IsLinearAnimation = isAnimation };
    }

    public unsafe RiveScene? GetScene(string name)
    {
        using var marshaledName = new MarshaledString(name);
        bool isAnimation = false;
        var sceneHandle = rive_ArtboardInstance_SceneByName(handle, marshaledName.Value, &isAnimation);
        if (sceneHandle == nint.Zero)
            return null;
        return new RiveScene(sceneHandle) { IsLinearAnimation = isAnimation };
    }

    /// <summary>Advances the artboard hierarchy; call with 0 after scrubbing to flush the applied pose.</summary>
    public void Advance(float seconds)
    {
        rive_ArtboardInstance_Advance(handle, seconds);
    }

    /// <summary>
    /// Sets the text of a named text-value run on the artboard (cables.gl-style direct control,
    /// independent of data binding). With an empty <paramref name="path"/> the run is found by name
    /// anywhere in this artboard (any group/layer depth); a non-empty path (e.g. "Outer/Inner")
    /// targets a run inside an embedded nested artboard. Returns false if no such run exists. Cheap
    /// to call every frame - the native setter is a no-op when the value is unchanged.
    /// </summary>
    public unsafe bool SetTextRun(string name, string value, string path = "")
    {
        using var marshaledName = new MarshaledString(name);
        using var marshaledPath = new MarshaledString(path ?? string.Empty);
        using var marshaledValue = new MarshaledString(value ?? string.Empty);
        return rive_ArtboardInstance_SetTextRun(handle, marshaledName.Value, marshaledPath.Value, marshaledValue.Value) != 0;
    }

    protected override bool ReleaseHandle()
    {
        rive_ArtboardInstance_Destroy(handle);
        return true;
    }

    public void BindViewModelInstance(RiveViewModelInstance riveViewModelInstance)
    {
        rive_Artboard_BindViewModelInstance(handle, riveViewModelInstance.InstanceHandle);
    }
}