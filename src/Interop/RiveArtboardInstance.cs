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