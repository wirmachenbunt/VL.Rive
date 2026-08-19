namespace VL.Rive.Interop;

// Specifies what to do with the render target at the beginning of a flush.
enum LoadAction
{
    Clear,
    PreserveRenderTarget,
    DontCare,
};

// Ordered dithering mode applied to gradients.
enum DitherMode
{
    None,
    InterleavedGradientNoise,
};

// Options for controlling how and where a frame is rendered.
// NOTE: The field order and types here must match rive::gpu::RenderContext::FrameDescriptor
// exactly, since this struct is passed to the native side by pointer (see BeginFrame).
struct FrameDescriptor
{
    public uint RenderTargetWidth;
    public uint RenderTargetHeight;
    public LoadAction LoadAction;
    public uint ClearColor;
    public int MsaaSampleCount;
    public bool DisableRasterOrdering;
    public DitherMode DitherMode;
    public uint VirtualTileWidth;
    public uint VirtualTileHeight;
    public bool Wireframe;
    public bool FillsDisabled;
    public bool StrokesDisabled;
    public bool ClockwiseFillOverride;

    // Explicit constructor to initialize fields with default values.
    public FrameDescriptor()
    {
        RenderTargetWidth = 0;
        RenderTargetHeight = 0;
        LoadAction = LoadAction.Clear;
        ClearColor = 0;
        MsaaSampleCount = 0;
        DisableRasterOrdering = false;
        DitherMode = DitherMode.InterleavedGradientNoise;
        VirtualTileWidth = 0;
        VirtualTileHeight = 0;
        Wireframe = false;
        FillsDisabled = false;
        StrokesDisabled = false;
        ClockwiseFillOverride = false;
    }
};
