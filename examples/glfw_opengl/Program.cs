using System.Reflection;
using Brutal.GlfwApi;
using Brutal.Logging;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Microsoft.Extensions.Logging;

var rootLogger = LogCategory.Make("Program");
LogSystem.Initialize(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

string ReadResource(string name)
{
    var assembly = Assembly.GetExecutingAssembly();
    var fullName = assembly.GetManifestResourceNames()
        .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
    using var stream = assembly.GetManifestResourceStream(fullName)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

float4x4 projection;
void UpdateViewport(int2 size)
{
    OpenGl.Viewport(0, 0, size.X, size.Y);
    var aspect = (float)size.X / (float)size.Y;
    projection = float4x4.CreateOrthographicOffCenter(-aspect, aspect, -1f, 1f, -1f, 1f);
    //projection = float4x4.CreateOrthographicOffCenter(-aspect, aspect, 1f, 1f, -1f, 1f);
}


if (!Glfw.Init())
{
    rootLogger.Critical(new Exception("Failed to initialize GLFW"));
    return;
}

Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 6);
Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
Glfw.WindowHint(GlfwWindowHint.Doublebuffer, Glfw.Constants.GLFW_TRUE);
Glfw.WindowHint(GlfwWindowHint.ScaleToMonitor, Glfw.Constants.GLFW_TRUE);
var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
{
    Title = "BRUTAL Rainbow Triangle",
    Size = new int2(1280, 720),
});
window.MakeContextCurrent();

unsafe
{
    var success = Glad.Initialize(Glfw.PInvoke.GetProcAddress);
    if (!success)
    {
        rootLogger.Critical("Failed to initialize OpenGL using Glad.");
        return;
    }
}
Glfw.SwapInterval(1);

// update viewport on framebuffer resize
window.OnFramebufferSize += (win, size) => UpdateViewport(size);
UpdateViewport(window.FramebufferSize);// init viewport

window.OnKey += (win, key, _, action, _) =>
{
    switch (key)
    {
        case GlfwKey.Escape:
            window.ShouldClose = true;
            break;
        default:
            rootLogger.Debug($"Key {key} {action}");
            break;
    }
};

float4 clearColor = new(0.2f, 0.3f, 0.3f, 1.0f);

var vertices = new float3[]
{// pos + color
    new(-0.5f, -0.5f, 0f), new(1f, 0f, 0f),
    new(0.5f, -0.5f, 0f), new(0f, 1f, 0f),
    new(0f,  0.5f, 0f), new(0f, 0f, 1f),
};
// stride of the float3
var float3Size = float3.ComponentSize * float3.ComponentCount;
// vertex array
var vao = OpenGl.GenVertexArray();
vao.Bind();
// vertex buffer
var vbo = OpenGl.GenBuffer();
vbo.Bind(GlBindTarget.ArrayBuffer);
OpenGl.BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
// vertices
OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, float3Size * 2, 0));
OpenGl.EnableVertexAttribArray(0);
// colors
OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, float3Size * 2, float3Size));
OpenGl.EnableVertexAttribArray(1);

var vertexShader = OpenGl.CreateShader(GlShaderType.Vertex);
vertexShader.SetSource(ReadResource("shader.vert"));
try
{
    vertexShader.Compile();
}
catch (GlShader.CompileException error)
{
    rootLogger.Critical(new Exception("Failed to compile vertex shader.", error));
    return;
}

var fragShader = OpenGl.CreateShader(GlShaderType.Fragment);
fragShader.SetSource(ReadResource("shader.frag"));
try
{
    fragShader.Compile();
}
catch (GlShader.CompileException error)
{
    rootLogger.Critical(new Exception("Failed to compile fragment shader.", error));
    return;
}

var program = OpenGl.CreateProgram();
program.Attach(vertexShader);
program.Attach(fragShader);
program.Link();
try
{
    fragShader.Compile();
}
catch (GlProgram.LinkException error)
{
    rootLogger.Critical(new Exception("Failed to link shader program.", error));
    return;
}
vertexShader.Dispose();
fragShader.Dispose();

while (!window.ShouldClose)
{
    Glfw.PollEvents();

    OpenGl.ClearColor(clearColor);
    OpenGl.Clear(GlClearBit.Color);

    program.Use();
    program.Uniform(0, projection);
    vao.Bind();
    OpenGl.Draw(GlTopology.Triangles, 3, 1, 0, 0);

    window.SwapBuffers();
}

// destroy shader program
program.Dispose();
OpenGl.DeleteVertexArrays(new Span<GlVertexArray>(ref vao));

window.Dispose();

Glfw.Terminate();
