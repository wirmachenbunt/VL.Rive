using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpDX.Direct3D11;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using VL.Core;
using VL.Core.Import;
using VL.Lib.Animation;
using VL.Lib.Collections;
using VL.Lib.Reactive;
using VL.Rive;
using VL.Rive.Interop;
using VL.Stride.Input;
using Path = VL.Lib.IO.Path;
using PixelFormat = SharpDX.DXGI.Format;

namespace VL.Rive;

[ProcessNode(HasStateOutput = true, FragmentSelection = FragmentSelection.Explicit)]
[Smell(SymbolSmell.Advanced)]
public sealed partial class RiveRenderer : RendererBase
{
    readonly AppHost appHost;
    readonly ILogger logger;

    RiveRenderContextD3D11? riveRenderContext;
    RiveRenderTargetD3D11? riveRenderTarget;

    Interop.RiveRenderer? riveRenderer;
    RiveFile? riveFile;
    Path? riveFilePath;
    RiveArtboardInstance? riveArtboard;
    RiveScene? riveScene;
    RiveViewModelInstance? riveViewModelInstance;
    IFrameClock frameClock;
    readonly IGraphicsDeviceService graphicsDeviceService;
    Int2 lastSize;
    RiveMat2D alignmentMat;

    readonly SerialDisposable inputSubscription = new SerialDisposable();
    IInputSource? lastInputSource;

    readonly SerialDisposable viewModelSubscription = new SerialDisposable();
    object? lastViewModel;
    int needToWrite;

    // Names of text runs we've already warned about as missing, so applying the
    // spread every frame doesn't spam the log. Cleared whenever the artboard changes.
    readonly HashSet<string> warnedMissingTextRuns = new();

    string? artboardName;
    string? sceneName;
    RiveFit riveFit;
    RiveAlignment riveAlignment;
    Optional<RectangleF> riveFrame;
    Optional<RectangleF> riveContent;
    float riveScaleFactor;

    [Fragment]
    [Smell(SymbolSmell.Internal)]
    public RiveRenderer([Pin(Visibility = Model.PinVisibility.Hidden)] NodeContext nodeContext)
    {
        appHost = nodeContext.AppHost;
        logger = nodeContext.GetLogger();
        frameClock = appHost.Services.GetRequiredService<IFrameClock>();
        graphicsDeviceService = appHost.Services.GetRequiredService<Game>().Services.GetService<IGraphicsDeviceService>();
    }

    [Fragment]
    [Smell(SymbolSmell.Internal)]
    public void Update(
        Path? file, 
        string? artboardName, 
        string? sceneName, 
        [Pin(Visibility = Model.PinVisibility.Optional)] RiveFit fit,
        [Pin(Visibility = Model.PinVisibility.Optional)] RiveAlignment alignment,
        [Pin(Visibility = Model.PinVisibility.Optional)] Optional<RectangleF> frame,
        [Pin(Visibility = Model.PinVisibility.Optional)] Optional<RectangleF> content,
        [Pin(Visibility = Model.PinVisibility.Optional)] [DefaultValue(1f)] float scaleFactor, 
        [DefaultValue(null)] object? viewModel,
        bool reload,
        [Pin(Visibility = Model.PinVisibility.Optional)] bool update,
        [Pin(Visibility = Model.PinVisibility.Optional)] bool externalTimeControl,
        [Pin(Visibility = Model.PinVisibility.Optional)] float progress,
        [Pin(Visibility = Model.PinVisibility.Optional)] Spread<RiveTextRun>? textRuns)
    {
        riveFit = fit;
        riveAlignment = alignment;
        riveFrame = frame;
        riveContent = content;
        riveScaleFactor = scaleFactor;

        // Native device can change - check on each update
        var nativeDevice = SharpDXInterop.GetNativeDevice(graphicsDeviceService.GraphicsDevice) as Device;
        if (riveRenderContext?.DevicePointer != nativeDevice?.NativePointer)
        {
            DisposeRiveResources();

            if (nativeDevice != null)
            {
                riveRenderContext = RiveRenderContextD3D11.Create(nativeDevice.NativePointer, nativeDevice.ImmediateContext.NativePointer);
                riveRenderer = riveRenderContext.CreateRenderer();
            }
        }

        // Load file
        if (reload || file != riveFilePath)
        {
            riveFilePath = file;

            DisposeRiveFileResources();

            var filePath = file?.ToString();
            if (!string.IsNullOrEmpty(filePath))
                riveFile = riveRenderContext?.LoadFile(file);
        }

        // Load artboard and view model instance
        // 'update' forces a fresh artboard instance (setup pose) so switching to a
        // short/single-frame timeline re-applies cleanly instead of blending on top of
        // whatever the previous timeline left on the reused artboard.
        if (update || riveArtboard is null || artboardName != this.artboardName)
        {
            this.artboardName = artboardName;

            // Resolve into a local first so an unknown name doesn't throw and freeze vvvv -
            // we only swap out the current artboard once we actually resolved a new one.
            RiveArtboardInstance? newArtboard;
            if (string.IsNullOrEmpty(artboardName))
                newArtboard = riveFile?.GetArtboardDefault();
            else
            {
                newArtboard = riveFile?.GetArtboard(artboardName);
                if (newArtboard is null && riveFile is not null)
                    logger.LogWarning("Rive artboard '{ArtboardName}' not found in file '{File}'. Keeping previous artboard.", artboardName, file);
            }

            // Swap when we resolved something, or when there's no file to resolve against (clear).
            if (newArtboard is not null || riveFile is null)
            {
                DisposeAndSetNull(ref riveArtboard);
                DisposeAndSetNull(ref riveScene);
                riveViewModelInstance = null;
                lastViewModel = null;

                riveArtboard = newArtboard;
                // Fresh artboard instance starts with the file's default text runs, so allow
                // missing-run warnings to fire again and let the spread re-apply below.
                warnedMissingTextRuns.Clear();

                if (riveArtboard != null)
                {
                    riveViewModelInstance = riveFile?.DefaultArtboardViewModel(riveArtboard);

                    // Bind at the ARTBOARD level, not the scene. Data binding lives on the
                    // artboard, and its property bindings drive the artboard's visuals for ANY
                    // scene type. Binding through the scene only works for state machines
                    // (StateMachineInstance overrides bindViewModelInstance) - the base Scene
                    // impl is empty, so binding through a linear-animation scene is a silent
                    // no-op and the view model gets ignored while a timeline plays. A state
                    // machine created afterwards (in the scene block below) inherits this
                    // binding via ArtboardInstance::stateMachineNamed -> inheritDataContext.
                    if (riveViewModelInstance != null)
                        riveArtboard.BindViewModelInstance(riveViewModelInstance);
                }
            }
        }

        // Load scene
        if (update || riveScene is null || sceneName != this.sceneName)
        {
            this.sceneName = sceneName;

            // Same fail-safe as the artboard: resolve first, only swap on success so an
            // unknown scene name keeps the previous scene instead of throwing.
            RiveScene? newScene;
            if (string.IsNullOrEmpty(sceneName))
                newScene = riveArtboard?.GetDefaultScene();
            else
            {
                newScene = riveArtboard?.GetScene(sceneName);
                if (newScene is null && riveArtboard is not null)
                    logger.LogWarning("Rive scene '{SceneName}' not found in artboard '{ArtboardName}' of file '{File}'. Keeping previous scene.", sceneName, artboardName, file);
            }

            if (newScene is not null || riveArtboard is null)
            {
                DisposeAndSetNull(ref riveScene);
                riveScene = newScene;

                // No scene-level bind here: the view model is bound on the artboard above,
                // which drives linear-animation scenes too and is inherited by state-machine
                // instances when they're created (see the artboard block).
            }
        }

        if (riveScene is null)
            return;

        // Named text runs - direct control independent of data binding. Applied every
        // frame (the native setter no-ops on unchanged text, so this is cheap) so it
        // also re-applies after the artboard is rebuilt.
        if (riveArtboard != null && textRuns != null)
        {
            foreach (var run in textRuns)
            {
                if (string.IsNullOrEmpty(run.Name))
                    continue;
                if (!riveArtboard.SetTextRun(run.Name, run.Value ?? string.Empty, run.Path ?? string.Empty)
                    && warnedMissingTextRuns.Add(run.Name))
                    logger.LogWarning("Rive text run '{TextRunName}' not found in artboard '{ArtboardName}' of file '{File}'.", run.Name, artboardName, file);
            }
        }

        if (viewModel != lastViewModel)
        {
            lastViewModel = viewModel;
            Interlocked.Increment(ref needToWrite);
            viewModelSubscription.Disposable = null;
            if (viewModel is IChannel c)
                viewModelSubscription.Disposable = c.ChannelOfObject.Subscribe(_ => Interlocked.Increment(ref needToWrite));
        }
        if (viewModel is IVLObject obj && !obj.Type.IsImmutable)
            Interlocked.Increment(ref needToWrite); // Force writing to Rive if the object is mutable

        // Write values to rive
        if (riveViewModelInstance != null && Interlocked.Exchange(ref needToWrite, 0) > 0)
            WriteValuesToRive(riveViewModelInstance, viewModel);

        if (externalTimeControl && riveScene.IsLinearAnimation)
        {
            // Drive the timeline position directly from 'progress' (0..1) instead of
            // advancing by the frame clock. This lets the patch scrub, reverse, or run
            // the animation at any speed. Not supported for state machines.
            var duration = riveScene.DurationSeconds;
            if (duration > 0f)
            {
                var normalized = Math.Clamp(progress, 0f, 1f);
                riveScene.SetTimeAndApply(normalized * duration);
                // Flush the applied pose through the artboard (transforms, constraints, ...).
                riveArtboard?.Advance(0f);
            }
        }
        else
        {
            riveScene.AdvanceAndApply((float)frameClock.TimeDifference);
        }

        if (riveViewModelInstance != null)
            ReadValuesFromRive(riveViewModelInstance, viewModel);
    }

    public string DumpFileAsJson()
    {
        if (riveFile is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        riveFile.WriteRiveFileAsJson(sb);
        return sb.ToString();
    }

    public RectangleF GetArtboardBounds()
    {
        if (riveArtboard is null)
            return RectangleF.Empty;
        var bounds = riveArtboard.Bounds;
        return new RectangleF(bounds.minX, bounds.minY, bounds.maxX - bounds.minX, bounds.maxY - bounds.minY);
    }

    protected override unsafe void DrawCore(RenderDrawContext context)
    {
        if (riveRenderContext is null || riveScene is null)
            return;

        // Subscribe to input events - in case we have many sinks we assume that there's only one input source active
        var inputSource = context.RenderContext.Tags.Get(InputExtensions.WindowInputSource);
        if (inputSource != lastInputSource)
        {
            lastInputSource = inputSource;
            inputSubscription.Disposable = SubscribeToInputSource(inputSource, context);
        }

        var renderTarget = context.CommandList.RenderTarget;
        if (renderTarget is null)
            return;

        var nativeRenderTarget = SharpDXInterop.GetNativeResource(renderTarget) as Texture2D;
        if (nativeRenderTarget is null)
            return;

        if (!IsSupportedByRive(nativeRenderTarget.Description.Format))
        {
            logger.LogError($"The render target format '{renderTarget.Format}' is not supported by Rive. In case you render to a texture set its format to RGBA_Typeless and its view to RGBA_Srgb.");
            return;
        }


        var frameDescriptor = new FrameDescriptor
        {
            RenderTargetWidth = (uint)renderTarget.Width,
            RenderTargetHeight = (uint)renderTarget.Height,
            LoadAction = LoadAction.PreserveRenderTarget,
        };
        riveRenderContext.BeginFrame(in frameDescriptor);

        alignmentMat = Methods.rive_ComputeAlignment(
            riveFit.ToNative(),
            riveAlignment.ToNative(),
            frame: riveFrame.ToNative(new RiveAABB(0, 0, renderTarget.Width, renderTarget.Height)),
            content: riveContent.ToNative(riveScene.Bounds), 
            scaleFactor: riveScaleFactor);

        riveRenderer!.Save();
        riveRenderer.Transform(in alignmentMat);
        riveScene.Draw(riveRenderer);
        riveRenderer.Restore();

        var size = new Int2(renderTarget.Width, renderTarget.Height);
        if (riveRenderTarget is null || lastSize != size)
        {
            lastSize = size;
            DisposeAndSetNull(ref riveRenderTarget);
            riveRenderTarget = riveRenderContext.MakeRenderTarget(size.X, size.Y);
        }
        riveRenderTarget.SetTargetTexture(nativeRenderTarget.NativePointer);

        riveRenderContext.Flush(riveRenderTarget);

        // Release render target texture
        riveRenderTarget.SetTargetTexture(default);

        // Restore Stride's pipeline state
        // TODO: Turn this into an official API in Stride
        context.CommandList.RestorePipelineState();

        // See submodules\rive-runtime\renderer\src\d3d11\render_context_d3d_impl.cpp
        static bool IsSupportedByRive(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.B8G8R8A8_UNorm:
                case PixelFormat.B8G8R8A8_Typeless:
                case PixelFormat.R8G8B8A8_UNorm:
                case PixelFormat.R8G8B8A8_Typeless:
                    return true;
            }
            return false;
        }
    }

    private void ReadValuesFromRive(RiveViewModelInstance riveViewModelInstance, object? viewModel)
    {
        if (viewModel is IChannel channel)
        {
            if (channel.Object is object o)
            {
                if (ReadIntoObject(riveViewModelInstance, ref o))
                {
                    channel.Object = o;
                }
            }
        }
        else if (viewModel is not null && viewModel.GetType() != typeof(object))
        {
            ReadIntoObject(riveViewModelInstance, ref viewModel);
        }

        bool ReadIntoObject(RiveViewModelInstance vm, ref object o)
        {
            var type = o.GetVLTypeInfo();

            var changed = false;
            foreach (var riveProp in vm.Properties)
            {
                var riveValue = riveProp.Value;
                if (!IsSupportedRiveType(riveValue.RiveType))
                    continue;

                // ViewModelRuntimeInstance has no changed detection / not modeled as RuntimeValue internally
                var hasChanged = riveValue.HasChanged;
                if (hasChanged.HasValue && !hasChanged.Value)
                    continue;

                // Acknowledge the change
                riveValue.ClearChanges();

                var prop = type.GetProperty(riveProp.Name);
                if (prop is null)
                    continue;

                var value = riveValue.Value;
                if (value is RiveViewModelInstance vmi)
                {
                    if (prop.GetValue(o) is object sub)
                    {
                        if (ReadIntoObject(vmi, ref sub))
                        {
                            changed = true;
                            // Set the value on the object
                            o = prop.WithValue(o, sub);
                        }
                    }
                }
                else if (value is RiveViewModelList riveList)
                {
                    if (prop.GetValue(o) is ISpread spread)
                    {
                        var newSpread = spread.ToBuilder();
                        newSpread.Clear();
                        var i = 0;
                        foreach (var item in riveList)
                        {
                            if (i < spread.Count && spread.GetItem(i) is object sub)
                            {
                                if (ReadIntoObject(item, ref sub))
                                {
                                    newSpread.Add(sub);
                                    changed = true;
                                }
                                else
                                    newSpread.Add(sub); // No change
                            }
                            else
                            {
                                var typeInfo = appHost.TypeRegistry.GetTypeInfo(spread.ElementType);
                                var instance = appHost.CreateInstance(typeInfo);
                                if (instance != null)
                                {
                                    ReadIntoObject(item, ref instance);
                                    newSpread.Add(instance);
                                    changed = true;
                                }
                            }
                        }
                        o = prop.WithValue(o, riveList.ToSpread());
                    }
                }
                else if (TryConvert(value, prop.Type.ClrType, out var vlValue))
                {
                    changed = true;
                    // Set the value on the object
                    o = prop.WithValue(o, vlValue);
                }
            }
            return changed;
        }
    }

    private void WriteValuesToRive(RiveViewModelInstance riveViewModel, object? viewModel)
    {
        if (viewModel is IChannel channel)
        {
            if (channel.Object is object o)
            {
                WriteFromObject(riveViewModel, o);
            }
        }
        else if (viewModel is not null && viewModel.GetType() != typeof(object))
        {
            WriteFromObject(riveViewModel, viewModel);
        }

        void WriteFromObject(RiveViewModelInstance vm, object o)
        {
            var type = o.GetVLTypeInfo();
            foreach (var riveProp in vm.Properties)
            {
                var riveValue = riveProp.Value;
                if (!IsSupportedRiveType(riveValue.RiveType))
                    continue;

                var prop = type.GetProperty(riveProp.Name);
                if (prop is null)
                    continue;

                if (riveValue.Value is RiveViewModelInstance vmi)
                {
                    if (prop.GetValue(o) is object sub)
                    {
                        WriteFromObject(vmi, sub);
                    }
                }
                else if (riveValue.Value is RiveViewModelList riveList)
                {
                    if (prop.GetValue(o) is ISpread spread)
                    {
                        // Clear the existing list and populate with spread items
                        var riveCount = riveList.Count;
                        for (int i = 0; i < spread.Count; i++)
                        {
                            var item = spread.GetItem(i);
                            if (item is null)
                                continue;

                            if (i < riveCount)
                                WriteFromObject(riveList[i], item);
                            else
                            {
                                // Find view model
                                foreach (var viewModel in riveFile!.ViewModels)
                                {
                                    if (viewModel.Name == item.GetVLTypeInfo().Name)
                                    {
                                        var instance = riveFile.CreateViewModelInstance(viewModel.Name);
                                        riveList.Add(instance);
                                        WriteFromObject(instance, item);
                                        break;
                                    }
                                }
                            }
                        }
                        for (int i = riveCount - 1; i >= spread.Count; i--)
                            riveList.RemoveAt(i);
                    }
                }
                else if (TryConvert(prop.GetValue(o), riveValue.Type, out var vlValue))
                {
                    riveValue.Value = vlValue;
                }
            }
        }
    }

    private static bool IsSupportedRiveType(RiveDataType type) => type switch
    {
        RiveDataType.String => true,
        RiveDataType.Number => true,
        RiveDataType.Boolean => true,
        RiveDataType.Color => true,
        RiveDataType.Integer => true,
        RiveDataType.ViewModel => true,
        RiveDataType.List => true,
        _ => false,
    };

    private static bool TryConvert(object? v, Type type, [NotNullWhen(true)] out object? result)
    {
        try
        {
            result = Convert.ChangeType(v, type);
            return result is not null;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    protected override void Destroy()
    {
        viewModelSubscription.Dispose();
        inputSubscription.Dispose();

        DisposeRiveResources();

        base.Destroy();
    }

    private void DisposeRiveResources()
    {
        DisposeRiveFileResources();
        DisposeRiveRenderResources();
    }

    private void DisposeRiveFileResources()
    {
        DisposeAndSetNull(ref riveScene);
        DisposeAndSetNull(ref riveArtboard);
        DisposeAndSetNull(ref riveFile);
    }

    private void DisposeRiveRenderResources()
    {
        DisposeAndSetNull(ref riveRenderer);
        DisposeAndSetNull(ref riveRenderTarget);
        DisposeAndSetNull(ref riveRenderContext);
    }

    static void DisposeAndSetNull<T>(ref T? resource) where T : class, IDisposable => Interlocked.Exchange(ref resource, null)?.Dispose();
}
