using Brutal.GlfwApi;
using Brutal.Logging;
using glfw_opengl;
using Microsoft.Extensions.Logging;

// GlTexture CreateTexture(ReadOnlySpan<byte> data, TextureLoader.FormatType formatType, out int width, out int height) {
//     GliTexture texture;
//     using (var iTexture = TextureLoader.LoadFromMemory(data, formatType).Using())
//     {
//         width = iTexture.Texture!.Width;
//         height = iTexture.Texture!.Height;
//         texture = GliConversionUtils.ToGliTexture(iTexture.Texture!);
//     }
//
//     GlTexture gpuTexture;
//     try
//     {
//         var target = GliConversionUtils.GetTarget(texture);
//
//         // create new texture
//         gpuTexture = new GlTexture();
//         OpenGl.GenTextures(new Span<GlTexture>(ref gpuTexture));
//         OpenGl.BindTexture((uint)target, gpuTexture.Id);
//         // set parameters
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_BASE_LEVEL, new ReadOnlySpan<int>(0));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_MAX_LEVEL, new ReadOnlySpan<int>(texture.LevelCount - 1));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_SWIZZLE_R, new ReadOnlySpan<int>((int)Brutal.GliApi.Gli.Swizzles.Identity.R));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_SWIZZLE_G, new ReadOnlySpan<int>((int)Brutal.GliApi.Gli.Swizzles.Identity.G));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_SWIZZLE_B, new ReadOnlySpan<int>((int)Brutal.GliApi.Gli.Swizzles.Identity.B));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_SWIZZLE_A, new ReadOnlySpan<int>((int)Brutal.GliApi.Gli.Swizzles.Identity.A));
//         // Setup filtering parameters for display
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_MIN_FILTER, new ReadOnlySpan<int>((int)OpenGl.GL_LINEAR));
//         OpenGl.TextureParameterInt(gpuTexture.Id, OpenGl.GL_TEXTURE_MAG_FILTER, new ReadOnlySpan<int>((int)OpenGl.GL_LINEAR));
//
//         var extent = texture.Extent;
//         var faceTotal = texture.LayerCount * texture.FaceCount;
//         switch (target)
//         {
//             case Brutal.GliApi.Gli.Target._1D:
//                 OpenGl.TexStorage1D((uint)target, texture.LevelCount, (uint)texture.Format, extent.X);
//                 break;
//             case Brutal.GliApi.Gli.Target._1DArray:
//             case Brutal.GliApi.Gli.Target._2D:
//             case Brutal.GliApi.Gli.Target.Cube:
//                 OpenGl.TexStorage2D((uint)target, texture.LevelCount, (uint)texture.Format, extent.X, target == Brutal.GliApi.Gli.Target._2D ? extent.Y : faceTotal);
//                 break;
//             case Brutal.GliApi.Gli.Target._2DArray:
//             case Brutal.GliApi.Gli.Target._3D:
//             case Brutal.GliApi.Gli.Target.CubeArray:
//                 OpenGl.TexStorage3D((uint)target, texture.LevelCount, (uint)texture.Format, extent.X, extent.Y, target == Brutal.GliApi.Gli.Target._3D ? extent.Z : faceTotal);
//                 break;
//             default:
//                 throw new System.Diagnostics.UnreachableException($"Unhandled texture target {target}");
//         }
//
//         // TODO: add layers, faces and levels like in https://github.com/g-truc/gli/blob/master/manual.md
//         for(uint layer = 0; layer < texture.LayerCount; ++layer)
//     	for(uint face = 0; face < texture.FaceCount; ++face)
//     	for(uint level = 0; level < texture.LevelCount; ++level)
//     	{
//             var layerGL = layer;
//             extent = texture.Extent((int)level);
//     		Target = gli::is_target_cube(Texture.target())
//     			? static_cast<GLenum>(GL_TEXTURE_CUBE_MAP_POSITIVE_X + face)
//     			: Target;
//
//     		switch(Texture.target())
//     		{
//     		case gli::TARGET_1D:
//     			if(gli::is_compressed(Texture.format()))
//     				glCompressedTexSubImage1D(
//     					Target, static_cast<GLint>(level), 0, Extent.x,
//     					Format.Internal, static_cast<GLsizei>(Texture.size(level)),
//     					Texture.data(layer, face, level));
//     			else
//     				glTexSubImage1D(
//     					Target, static_cast<GLint>(level), 0, Extent.x,
//     					Format.External, Format.Type,
//     					Texture.data(layer, face, level));
//     			break;
//     		case gli::TARGET_1D_ARRAY:
//     		case gli::TARGET_2D:
//     		case gli::TARGET_CUBE:
//     			if(gli::is_compressed(Texture.format()))
//     				glCompressedTexSubImage2D(
//     					Target, static_cast<GLint>(level),
//     					0, 0,
//     					Extent.x,
//     					Texture.target() == gli::TARGET_1D_ARRAY ? LayerGL : Extent.y,
//     					Format.Internal, static_cast<GLsizei>(Texture.size(level)),
//     					Texture.data(layer, face, level));
//     			else
//     				glTexSubImage2D(
//     					Target, static_cast<GLint>(level),
//     					0, 0,
//     					Extent.x,
//     					Texture.target() == gli::TARGET_1D_ARRAY ? LayerGL : Extent.y,
//     					Format.External, Format.Type,
//     					Texture.data(layer, face, level));
//     			break;
//     		case gli::TARGET_2D_ARRAY:
//     		case gli::TARGET_3D:
//     		case gli::TARGET_CUBE_ARRAY:
//     			if(gli::is_compressed(Texture.format()))
//     				glCompressedTexSubImage3D(
//     					Target, static_cast<GLint>(level),
//     					0, 0, 0,
//     					Extent.x, Extent.y,
//     					Texture.target() == gli::TARGET_3D ? Extent.z : LayerGL,
//     					Format.Internal, static_cast<GLsizei>(Texture.size(level)),
//     					Texture.data(layer, face, level));
//     			else
//     				glTexSubImage3D(
//     					Target, static_cast<GLint>(level),
//     					0, 0, 0,
//     					Extent.x, Extent.y,
//     					Texture.target() == gli::TARGET_3D ? Extent.z : LayerGL,
//     					Format.External, Format.Type,
//     					Texture.data(layer, face, level));
//     			break;
//     		default: assert(0); break;
//     		}
//     	}
//     }
//     finally
//     {
//         texture.Destroy();
//     }
//
//     return gpuTexture;
// }

internal static class Program
{
    public static void Main() {
        // init logging system
        LogSystem.Initialize(builder => builder.AddConsole().AddProvider(App.Console).SetMinimumLevel(LogLevel.Debug));

        // https://www.glfw.org/docs/latest/intro_guide.html#init_hints
        // Glfw.SetInitHint(GlfwInitHint.Platform, Glfw.Constants.GLFW_PLATFORM_WAYLAND);
        // Glfw.InitAllocator()

        // init Glfw
        if (!Glfw.Init())
            throw new Exception("Failed to initialize GLFW");

        // init and run app
        try {
            using var app = new glfw_opengl.App();
            app.Run();
        } catch (Exception error) {
            App.Log.Critical(error);
        } finally {
            // de-initialize Glfw
            Glfw.Terminate();
        }
    }
}
