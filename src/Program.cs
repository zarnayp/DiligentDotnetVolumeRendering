using Diligent;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DiligentVolumeRendering
{
    using IDeviceContext = Diligent.IDeviceContext;

    static class Program
    {
        static void SetMessageCallback(IEngineFactory engineFactory)
        {
            engineFactory.SetMessageCallback((severity, message, function, file, line) =>
            {
                switch (severity)
                {
                    case DebugMessageSeverity.Warning:
                    case DebugMessageSeverity.Error:
                    case DebugMessageSeverity.FatalError:
                        Console.WriteLine($"Diligent Engine: {severity} in {function}() ({file}, {line}): {message}");
                        break;
                    case DebugMessageSeverity.Info:
                        Console.WriteLine($"Diligent Engine: {severity} {message}");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(severity), severity, null);
                }
            });
        }

        static void CreateRenderDeviceAndSwapChain(IEngineFactory engineFactory, out IRenderDevice renderDevice, out IDeviceContext deviceContext, out ISwapChain swapChain, IntPtr handle)
        {
            IDeviceContext[] contextsOut;

            switch (engineFactory)
            {
                case IEngineFactoryD3D11 engineFactoryD3D11:
                    engineFactoryD3D11.CreateDeviceAndContextsD3D11(new()
                    {
                        EnableValidation = true
                    }, out renderDevice, out contextsOut);
                    swapChain = engineFactoryD3D11.CreateSwapChainD3D11(renderDevice, contextsOut[0], new(), new(), new() { Wnd = handle });
                    break;
                case IEngineFactoryD3D12 engineFactoryD3D12:
                    engineFactoryD3D12.CreateDeviceAndContextsD3D12(new()
                    {
                        EnableValidation = true
                    }, out renderDevice, out contextsOut);
                    swapChain = engineFactoryD3D12.CreateSwapChainD3D12(renderDevice, contextsOut[0], new(), new(), new() { Wnd = handle });
                    break;
                case IEngineFactoryVk engineFactoryVk:
                    engineFactoryVk.CreateDeviceAndContextsVk(new()
                    {
                        EnableValidation = false
                    }, out renderDevice, out contextsOut);
                    swapChain = engineFactoryVk.CreateSwapChainVk(renderDevice, contextsOut[0], new(), new() { Wnd = handle });
                    break;
                case IEngineFactoryOpenGL engineFactoryOpenGL:
                    engineFactoryOpenGL.CreateDeviceAndSwapChainGL(new()
                    {
                        EnableValidation = true,
                        Window = new() { Wnd = handle }
                    }, out renderDevice, out var glContext, new(), out swapChain);
                    contextsOut = glContext != null ? new[] { glContext } : null;
                    break;
                default:
                    throw new NotSupportedException("Unknown engine factory");
            }

            deviceContext = contextsOut[0];
        }

        static void Main(string[] args)
        {
            using var window = new Form()
            {
                Text = "Volume renderer",
                FormBorderStyle = FormBorderStyle.Sizable,
                ClientSize = new Size(1024, 720),
                StartPosition = FormStartPosition.CenterScreen,
                MinimumSize = new Size(200, 200)
            };

            using IEngineFactory engineFactory = Native.CreateEngineFactory<IEngineFactoryD3D11>();

            SetMessageCallback(engineFactory);
            CreateRenderDeviceAndSwapChain(engineFactory, out var renderDeviceOut, out var deviceContextOut, out var swapChainOut, window.Handle);
            using var renderDevice = renderDeviceOut;
            using var deviceContext = deviceContextOut;
            using var swapChain = swapChainOut;

            using var volumeRenderer = new VolumeRenderer(renderDevice, deviceContext, engineFactory);

            var clock = new Stopwatch();
            clock.Start();

            window.Paint += (sender, e) =>
            {
                var rtv = swapChain.GetCurrentBackBufferRTV();
                var dsv = swapChain.GetDepthBufferDSV();

                volumeRenderer.Render(renderDevice, deviceContext, rtv, (int)swapChain.GetDesc().Width, (int)swapChain.GetDesc().Height);

                swapChain.Present(0);
                window.Invalidate(new Rectangle(0, 0, 1, 1)); //HACK for Vulkan
            };

            window.Resize += (sender, e) =>
            {
                var control = (Control)sender!;
                swapChain.Resize((uint)control.Width, (uint)control.Height, SurfaceTransform.Optimal);
            };
            Application.Run(window);
        }
    }
}
