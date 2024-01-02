using Diligent;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Numerics;

namespace DiligentVolumeRendering
{
    using IDeviceContext = Diligent.IDeviceContext;

    internal static class Utils
    {
        public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlaneDistance, float farPlaneDistance, bool isOpenGL)
        {
            if (fieldOfView <= 0.0f || fieldOfView >= MathF.PI)
                throw new ArgumentOutOfRangeException(nameof(fieldOfView));

            if (nearPlaneDistance <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

            if (farPlaneDistance <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

            if (nearPlaneDistance >= farPlaneDistance)
                throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));

            float yScale = 1.0f / MathF.Tan(fieldOfView * 0.5f);
            float xScale = yScale / aspectRatio;

            Matrix4x4 result = new()
            {
                M11 = xScale,
                M22 = yScale
            };

            if (isOpenGL)
            {
                result.M33 = (farPlaneDistance + nearPlaneDistance) / (farPlaneDistance - nearPlaneDistance);
                result.M43 = -2 * nearPlaneDistance * farPlaneDistance / (farPlaneDistance - nearPlaneDistance);
                result.M34 = 1.0f;
            }
            else
            {
                result.M33 = farPlaneDistance / (farPlaneDistance - nearPlaneDistance);
                result.M43 = -nearPlaneDistance * farPlaneDistance / (farPlaneDistance - nearPlaneDistance);
                result.M34 = 1.0f;
            }

            return result;
        }

        public static ITexture LoadVolume(IRenderDevice device, IDeviceContext deviceContext, string fileName)
        {
            var descritpion = new TextureDesc { 
                Type = ResourceDimension.Tex3d,
                Name = "Volume",
                Height = 256,
                Width = 256,
                ArraySizeOrDepth = 256,
                Format = TextureFormat.R8_UNorm,
                Usage = Usage.Default,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.Write
            };

            using var mmf = MemoryMappedFile.CreateFromFile(fileName, FileMode.Open);
            using var accessor = mmf.CreateViewAccessor();

            long fileSize = new FileInfo(fileName).Length;
            byte[] byteArray = new byte[fileSize];
            accessor.ReadArray(0, byteArray, 0, (int)fileSize);

            unsafe
            {
                fixed (byte* ptr = byteArray)
                {
                    var rawData = new TextureData
                    {
                        Context = deviceContext,
                        SubResources = new TextureSubResData[]
                            {
                                new()
                                {
                                    Data = (IntPtr)ptr,
                                    Stride = 256,
                                    DepthStride = 256*256
                                }
                            }
                    };
                    var texture = device.CreateTexture(descritpion, rawData);
                    return texture;
                }
            }
        }
    }
}
