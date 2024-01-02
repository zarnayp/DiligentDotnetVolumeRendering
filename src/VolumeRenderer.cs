using Diligent;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DiligentVolumeRendering
{
    using IDeviceContext = IDeviceContext;

    internal class VolumeRenderer : IDisposable
    {
        struct Vertex
        {
            public Vector3 Position;

            public Vertex(Vector3 position)
            {
                Position = position;
            }
        }

        struct WindowSize
        {
            public float Width;
            public float Height;
        }

        IBuffer uniformBuffer;
        IBuffer windowSizeBuffer;
        IBuffer vertexBuffer;
        IBuffer indexBuffer;

        IPipelineState pipelineCubeFront;
        IPipelineState pipelineCubeBack;
        IPipelineState pipelineRaycaster;

        IShaderResourceBinding shaderResourceBindingBack;
        IShaderResourceBinding shaderResourceBindingFront;
        IShaderResourceBinding shaderResourceBindingRayCasting;

        ITexture frontCubeTexture;
        ITexture backCubeTexture;
        ITexture volumeTexture;

        ITextureView frontCubeTextureSrv;
        ITextureView frontCubeTextureRtv;
        ITextureView backCubeTextureSrv;
        ITextureView backCubeTextureRtv;

        Stopwatch clock = new Stopwatch();

        public VolumeRenderer(IRenderDevice renderDevice, IDeviceContext deviceContext, IEngineFactory engineFactory)
        {
            var cubeVertices = new Vertex[] {
                new Vertex(new(-1, -1, -1)),
                new Vertex(new(-1, -1, 1)),
                new Vertex(new(-1, 1, -1)),
                new Vertex(new(-1, 1, 1) ),
                new Vertex(new(1, -1, -1)),
                new Vertex(new(1, -1, 1) ),
                new Vertex(new(1, 1, -1) ),
                new Vertex(new(1, 1, 1)  )
            };

            var cubeIndices = new uint[]
            {
                0, 1, 2,
                2, 1, 3,
                0, 4, 1,
                1, 4, 5,
                0, 2, 4,
                4, 2, 6,
                1, 5, 3,
                3, 5, 7,
                2, 3, 6,
                6, 3, 7,
                5, 4, 7,
                7, 4, 6,
            };

            using var shaderSourceFactory = engineFactory.CreateDefaultShaderSourceStreamFactory("assets");

            // Create buffers
            vertexBuffer = renderDevice.CreateBuffer(new()
            {
                Name = "Cube vertex buffer",
                Usage = Usage.Immutable,
                BindFlags = BindFlags.VertexBuffer,
                Size = (ulong)(Unsafe.SizeOf<Vertex>() * cubeVertices.Length)
            }, cubeVertices);

            indexBuffer = renderDevice.CreateBuffer(new()
            {
                Name = "Cube index buffer",
                Usage = Usage.Immutable,
                BindFlags = BindFlags.IndexBuffer,
                Size = (ulong)(Unsafe.SizeOf<uint>() * cubeIndices.Length)
            }, cubeIndices);

            uniformBuffer = renderDevice.CreateBuffer(new()
            {
                Name = "Uniform buffer",
                Size = (ulong)Unsafe.SizeOf<Matrix4x4>(),
                Usage = Usage.Dynamic,
                BindFlags = BindFlags.UniformBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });

            windowSizeBuffer = renderDevice.CreateBuffer(new()
            {
                Name = "Window size buffer",
                Size = (ulong)Unsafe.SizeOf<WindowSize>(),
                Usage = Usage.Dynamic,
                BindFlags = BindFlags.UniformBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            });
            

            // Back and front cube pipelines
            var vs = renderDevice.CreateShader(new()
            {
                FilePath = "cube.vsh",
                ShaderSourceStreamFactory = shaderSourceFactory,
                Desc = new()
                {
                    Name = "Cube VS", ShaderType = ShaderType.Vertex, UseCombinedTextureSamplers = true
                },
                SourceLanguage = ShaderSourceLanguage.Hlsl
            }, out _);

            var ps = renderDevice.CreateShader(new()
            {
                FilePath = "cube.psh",
                ShaderSourceStreamFactory = shaderSourceFactory,
                Desc = new()
                {
                    Name = "Cube PS", ShaderType = ShaderType.Pixel, UseCombinedTextureSamplers = true
                },
                SourceLanguage = ShaderSourceLanguage.Hlsl
            }, out _);


            pipelineCubeFront = renderDevice.CreateGraphicsPipelineState(new()
            {
                PSODesc = new() { Name = "pipelineCubeFront" },
                Vs = vs,
                Ps = ps,
                GraphicsPipeline = new()
                {
                    InputLayout = new()
                    {
                        LayoutElements = new[] {
                        new LayoutElement
                        {
                            InputIndex = 0,
                            NumComponents = 3,
                            ValueType = Diligent.ValueType.Float32,
                            IsNormalized = false,
                        }
                    }
                    },
                    PrimitiveTopology = PrimitiveTopology.TriangleList,
                    RasterizerDesc = new() { CullMode = CullMode.Front },
                    DepthStencilDesc = new() { DepthEnable = true },
                    NumRenderTargets = 1,
                }
            });

            pipelineCubeFront.GetStaticVariableByName(ShaderType.Vertex, "Constants").Set(uniformBuffer, SetShaderResourceFlags.None);
            shaderResourceBindingFront = pipelineCubeFront.CreateShaderResourceBinding(true);

            pipelineCubeBack = renderDevice.CreateGraphicsPipelineState(new()
            {
                PSODesc = new() { Name = "pipelineCubeBack" },
                Vs = vs,
                Ps = ps,
                GraphicsPipeline = new()
                {
                    InputLayout = new()
                    {
                        LayoutElements = new[] {
                        new LayoutElement
                        {
                            InputIndex = 0,
                            NumComponents = 3,
                            ValueType = Diligent.ValueType.Float32,
                            IsNormalized = false,
                        }
                    }
                    },
                    PrimitiveTopology = PrimitiveTopology.TriangleList,
                    RasterizerDesc = new() { CullMode = CullMode.Back },
                    DepthStencilDesc = new() { DepthEnable = true },
                    NumRenderTargets = 1,
                }
            });

            pipelineCubeBack.GetStaticVariableByName(ShaderType.Vertex, "Constants").Set(uniformBuffer, SetShaderResourceFlags.None);
            shaderResourceBindingBack = pipelineCubeBack.CreateShaderResourceBinding(true);

            // Raycasting pipeline
            volumeTexture = Utils.LoadVolume(renderDevice, deviceContext, "assets/skull_256x256x256_uint8.raw");
            var volumeTextureSrv = volumeTexture.GetDefaultView(TextureViewType.ShaderResource); ;

            var rayCastingVS = renderDevice.CreateShader(new()
            {
                FilePath = "rayCasting.vsh",
                ShaderSourceStreamFactory = shaderSourceFactory,
                Desc = new()
                {
                    Name = "Ray tracing VS",
                    ShaderType = ShaderType.Vertex,
                    UseCombinedTextureSamplers = true,

                },
                SourceLanguage = ShaderSourceLanguage.Hlsl,

            }, out _);

            var rayCastingPS = renderDevice.CreateShader(new()
            {
                FilePath = "rayCasting.psh",
                ShaderSourceStreamFactory = shaderSourceFactory,
                Desc = new()
                {
                    Name = "Ray casting PS",
                    ShaderType = ShaderType.Pixel,
                    UseCombinedTextureSamplers = true
                },
                SourceLanguage = ShaderSourceLanguage.Hlsl,

            }, out _);

            var rayCastingPipelineStateDesc = new PipelineStateDesc();
            rayCastingPipelineStateDesc.Name = "RayCasting PSO";
            rayCastingPipelineStateDesc.ResourceLayout = new()
            {
                DefaultVariableType = ShaderResourceVariableType.Static,
                Variables = new ShaderResourceVariableDesc[] {
                        new ()
                        {
                            ShaderStages = ShaderType.Pixel,
                            Name = "txVolume",
                            Type = ShaderResourceVariableType.Mutable,

                        },
                        new ()
                        {
                            ShaderStages = ShaderType.Pixel,
                            Name = "txPositionFront",
                            Type = ShaderResourceVariableType.Mutable,

                        },
                        new ()
                        {
                            ShaderStages = ShaderType.Pixel,
                            Name = "txPositionBack",
                            Type = ShaderResourceVariableType.Mutable,

                        }
                        },
                ImmutableSamplers = new ImmutableSamplerDesc[] {
                        new ()
                        {
                            Desc = new()
                            {
                                MinFilter = FilterType.Linear, MagFilter = FilterType.Linear, MipFilter = FilterType.Linear,
                                AddressU = TextureAddressMode.Border, AddressV = TextureAddressMode.Border, AddressW = TextureAddressMode.Border,
                                ComparisonFunc = ComparisonFunction.Never,
                                MinLOD = 0,
                            },
                            SamplerOrTextureName = "txPositionFront",
                            ShaderStages = ShaderType.Pixel
                        } }
            };

            pipelineRaycaster = renderDevice.CreateGraphicsPipelineState(new()
            {
                PSODesc = rayCastingPipelineStateDesc,
                Vs = rayCastingVS,
                Ps = rayCastingPS,
                GraphicsPipeline = new()
                {
                    InputLayout = new()
                    {
                        LayoutElements = new[] {
                        new LayoutElement
                        {
                            InputIndex = 0,
                            NumComponents = 3,
                            ValueType = Diligent.ValueType.Float32,
                            IsNormalized = false,
                        }
                    }
                    },
                    PrimitiveTopology = PrimitiveTopology.TriangleList,
                    RasterizerDesc = new() { CullMode = CullMode.Back },
                    DepthStencilDesc = new() { DepthEnable = true },
                    NumRenderTargets = 1
                }
            });

            pipelineRaycaster.GetStaticVariableByName(ShaderType.Vertex, "Constants").Set(uniformBuffer, SetShaderResourceFlags.None);
            pipelineRaycaster.GetStaticVariableByName(ShaderType.Pixel, "Constants").Set(windowSizeBuffer, SetShaderResourceFlags.None);
            shaderResourceBindingRayCasting = pipelineRaycaster.CreateShaderResourceBinding(true);

            var shaderVolumeVariable = shaderResourceBindingRayCasting.GetVariableByName(ShaderType.Pixel, "txVolume");
            shaderVolumeVariable.Set(volumeTextureSrv, SetShaderResourceFlags.None);

            const int width = 1024;
            const int height = 720;

            CreateTextureViews(renderDevice, width, height);

            clock.Start();
        }

        public void Render(IRenderDevice renderDevice, IDeviceContext deviceContext, ITextureView rtv, int width, int height)
        {

            var isOpenGL = renderDevice.GetDeviceInfo().Type == RenderDeviceType.Gl || renderDevice.GetDeviceInfo().Type == RenderDeviceType.Gles;

            // Uniform buffer for mvp
            var worldMatrix = Matrix4x4.CreateRotationZ(clock.ElapsedMilliseconds / 6000.0f) * Matrix4x4.CreateRotationX(-MathF.PI / 2);
            var viewMatrix = Matrix4x4.CreateTranslation(0.0f, 0.0f, 5.0f);
            var projMatrix = Utils.CreatePerspectiveFieldOfView(MathF.PI / 5.0f, width / (float)height, 0.01f, 100.0f, isOpenGL);
            var wvpMatrix = Matrix4x4.Transpose(worldMatrix * viewMatrix * projMatrix);
            var mapUniformBuffer = deviceContext.MapBuffer<Matrix4x4>(uniformBuffer, MapType.Write, MapFlags.Discard);

            mapUniformBuffer[0] = wvpMatrix;
            deviceContext.UnmapBuffer(uniformBuffer, MapType.Write);

            //Window size buffer
            var mapWindowSizeBuffer = deviceContext.MapBuffer<WindowSize>(windowSizeBuffer, MapType.Write, MapFlags.Discard);
            mapWindowSizeBuffer[0] = new WindowSize() { Width = 1 / (float)width , Height = 1 / (float)height };
            deviceContext.UnmapBuffer(windowSizeBuffer, MapType.Write);

            // Back cube pipeline 
            deviceContext.SetPipelineState(pipelineCubeBack);
            deviceContext.SetVertexBuffers(0, new[] { vertexBuffer }, new[] { 0ul }, ResourceStateTransitionMode.Transition);
            deviceContext.SetIndexBuffer(indexBuffer, 0, ResourceStateTransitionMode.Transition);
            deviceContext.CommitShaderResources(shaderResourceBindingBack, ResourceStateTransitionMode.Transition);
            deviceContext.ClearRenderTarget(backCubeTextureRtv, new(0.0f, 0.0f, 0.0f, 1.0f), ResourceStateTransitionMode.Transition);
            deviceContext.SetRenderTargets(new[] { backCubeTextureRtv }, null, ResourceStateTransitionMode.Transition);
            deviceContext.DrawIndexed(new()
            {
                IndexType = Diligent.ValueType.UInt32,
                NumIndices = 36,
                Flags = DrawFlags.VerifyAll
            });

            // Front cube pipeline
            deviceContext.SetPipelineState(pipelineCubeFront);
            deviceContext.SetVertexBuffers(0, new[] { vertexBuffer }, new[] { 0ul }, ResourceStateTransitionMode.Transition);
            deviceContext.SetIndexBuffer(indexBuffer, 0, ResourceStateTransitionMode.Transition);
            deviceContext.CommitShaderResources(shaderResourceBindingFront, ResourceStateTransitionMode.Transition);
            deviceContext.ClearRenderTarget(frontCubeTextureRtv, new(0.0f, 0.0f, 0.0f, 1.0f), ResourceStateTransitionMode.Transition);
            deviceContext.SetRenderTargets(new[] { frontCubeTextureRtv }, null, ResourceStateTransitionMode.Transition);
            deviceContext.DrawIndexed(new()
            {
                IndexType = Diligent.ValueType.UInt32,
                NumIndices = 36,
                Flags = DrawFlags.VerifyAll
            });

            // Ray casting pipeline
            deviceContext.SetPipelineState(pipelineRaycaster);
            deviceContext.SetVertexBuffers(0, new[] { vertexBuffer }, new[] { 0ul }, ResourceStateTransitionMode.Transition);
            deviceContext.SetIndexBuffer(indexBuffer, 0, ResourceStateTransitionMode.Transition);
            deviceContext.ClearRenderTarget(rtv, new(0.0f, 0.0f, 0.0f, 1.0f), ResourceStateTransitionMode.Transition);
            deviceContext.SetRenderTargets(new[] { rtv }, null, ResourceStateTransitionMode.Transition);
            deviceContext.CommitShaderResources(shaderResourceBindingRayCasting, ResourceStateTransitionMode.Transition);
            deviceContext.DrawIndexed(new()
            {
                IndexType = Diligent.ValueType.UInt32,
                NumIndices = 36,
                Flags = DrawFlags.VerifyAll
            });
        }

        private void CreateTextureViews(IRenderDevice renderDevice, int width, int height)
        {
            var cubeTextureDesritpion = new TextureDesc()
            {
                Type = ResourceDimension.Tex2d,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                Usage = Usage.Default,
                Format = TextureFormat.RGBA32_Float,
                Height = (uint)height,
                Width = (uint)width,

            };

            frontCubeTexture = renderDevice.CreateTexture(cubeTextureDesritpion);
            frontCubeTextureSrv = frontCubeTexture.GetDefaultView(TextureViewType.ShaderResource);
            frontCubeTextureRtv = frontCubeTexture.GetDefaultView(TextureViewType.RenderTarget);

            backCubeTexture = renderDevice.CreateTexture(cubeTextureDesritpion);
            backCubeTextureSrv = backCubeTexture.GetDefaultView(TextureViewType.ShaderResource);
            backCubeTextureRtv = backCubeTexture.GetDefaultView(TextureViewType.RenderTarget);

            var shaderFrontVariable = shaderResourceBindingRayCasting.GetVariableByName(ShaderType.Pixel, "txPositionFront");
            shaderFrontVariable.Set(frontCubeTextureSrv, SetShaderResourceFlags.None);

            var shaderBackVariable = shaderResourceBindingRayCasting.GetVariableByName(ShaderType.Pixel, "txPositionBack");
            shaderBackVariable.Set(backCubeTextureSrv, SetShaderResourceFlags.None);
        }

        public void Dispose()
        {
            uniformBuffer.Dispose();
            windowSizeBuffer.Dispose();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();

            frontCubeTexture.Dispose();
            backCubeTexture.Dispose();
            volumeTexture.Dispose();

            shaderResourceBindingBack.Dispose();
            shaderResourceBindingFront.Dispose();
            shaderResourceBindingRayCasting.Dispose();

            pipelineCubeFront.Dispose();
            pipelineCubeBack.Dispose();
            pipelineRaycaster.Dispose();

        }
    }

}
