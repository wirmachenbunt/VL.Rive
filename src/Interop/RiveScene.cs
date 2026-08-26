using System.Runtime.InteropServices;
using static VL.Rive.Interop.Methods;

namespace VL.Rive.Interop;

internal class RiveScene : RiveObject
{
    public unsafe RiveScene(nint handle) : base(handle) 
    {
        var namePtr = rive_Scene_Name(handle);
        Name = SpanExtensions.AsString(namePtr);
        NativeMemory.Free(namePtr);
    }

    public unsafe string Name { get; }

    /// <summary>
    /// True if this scene is a linear animation timeline (scrubbable via <see cref="SetTimeAndApply"/>),
    /// false for state machines. Set by the artboard when the scene is created.
    /// </summary>
    public bool IsLinearAnimation { get; internal set; }

    /// <summary>Duration of the timeline in seconds. -1 for continuous scenes (state machines).</summary>
    public float DurationSeconds
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return rive_Scene_DurationSeconds(handle);
        }
    }

    /// <summary>
    /// Scrubs a linear-animation timeline to an absolute time (seconds) and applies it.
    /// Only valid when <see cref="IsLinearAnimation"/> is true. The artboard must be advanced
    /// afterwards (advance by 0) so the applied pose is flushed to the render hierarchy.
    /// </summary>
    public void SetTimeAndApply(float seconds)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        rive_Scene_SetTimeAndApply(handle, seconds);
    }

    public RiveAABB Bounds
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsClosed, this);
            return rive_Scene_Bounds(handle);
        }
    }

    public void AdvanceAndApply(float elapsedSeconds)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        rive_Scene_AdvanceAndApply(handle, elapsedSeconds);
    }

    public void Draw(RiveRenderer renderer)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        rive_Scene_Draw(handle, renderer.DangerousGetHandle());
    }

    public RiveHitResult PointerDown(float x, float y, int pointerId)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return rive_Scene_PointerDown(handle, x, y, pointerId);
    }

    public RiveHitResult PointerMove(float x, float y, float timeStamp, int pointerId)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return rive_Scene_PointerMove(handle, x, y, timeStamp, pointerId);
    }

    public RiveHitResult PointerUp(float x, float y, int pointerId)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return rive_Scene_PointerUp(handle, x, y, pointerId);
    }

    public RiveHitResult PointerExit(float x, float y, int pointerId)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return rive_Scene_PointerExit(handle, x, y, pointerId);
    }

    protected override bool ReleaseHandle()
    {
        rive_Scene_Destroy(handle);
        return true;
    }

    internal void BindViewModelInstance(RiveViewModelInstance riveViewModelInstance)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        rive_Scene_BindViewModelInstance(handle, riveViewModelInstance.InstanceHandle);
    }
}