using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenPortal.Host.Encode;

/// <summary>
/// Converte (e escala, se preciso) BGRA → NV12 na GPU com o ID3D11VideoProcessor.
/// Mantém um anel de texturas NV12 para o encoder poder ler uma enquanto a próxima é escrita.
/// </summary>
public sealed class Nv12Converter : IDisposable
{
    public const int RingSize = 4;

    private readonly ID3D11Device _device;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11Texture2D[] _outputs = new ID3D11Texture2D[RingSize];
    private readonly ID3D11VideoProcessorOutputView[] _outputViews = new ID3D11VideoProcessorOutputView[RingSize];
    private readonly VideoProcessorStream[] _streams = new VideoProcessorStream[1];
    private int _next;

    public int OutputWidth { get; }
    public int OutputHeight { get; }

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, int inputWidth, int inputHeight, int outputWidth, int outputHeight)
    {
        _device = device;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)inputWidth,
            InputHeight = (uint)inputHeight,
            OutputWidth = (uint)outputWidth,
            OutputHeight = (uint)outputHeight,
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
            Usage = VideoUsage.OptimalSpeed,
        };
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        for (int i = 0; i < RingSize; i++)
        {
            _outputs[i] = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)outputWidth,
                Height = (uint)outputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            _outputViews[i] = _videoDevice.CreateVideoProcessorOutputView(_outputs[i], _enumerator,
                new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });
        }
    }

    /// <summary>Converte para a próxima textura do anel e devolve o índice dela.</summary>
    public int Convert(ID3D11Texture2D bgra)
    {
        int slot = _next;
        _next = (_next + 1) % RingSize;

        using var inputView = _videoDevice.CreateVideoProcessorInputView(bgra, _enumerator,
            new VideoProcessorInputViewDescription { ViewDimension = VideoProcessorInputViewDimension.Texture2D });

        _streams[0] = new VideoProcessorStream { Enable = true, InputSurface = inputView };
        _videoContext.VideoProcessorBlt(_processor, _outputViews[slot], 0, 1, _streams).CheckError();
        _streams[0] = default;
        return slot;
    }

    public ID3D11Texture2D GetOutput(int slot) => _outputs[slot];

    public void Dispose()
    {
        for (int i = 0; i < RingSize; i++) { _outputViews[i]?.Dispose(); _outputs[i]?.Dispose(); }
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
