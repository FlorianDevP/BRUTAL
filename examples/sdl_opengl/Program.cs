using System.Reflection;
using System.Runtime.InteropServices;
using Brutal.SdlApi;
using Brutal.Logging;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Microsoft.Extensions.Logging;
using Brutal.Enums;

var rootLogger = LogCategory.Make("Program");
Brutal.Logging.LogSystem.Initialize(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

static void SetAppMetadata(string appname, string appversion, string appidentifier)
{
    if (!Sdl.Init.SetAppMetadata(appname, appversion, appidentifier))
    {
        throw SdlException.FromError("SDL_SetAppMetadata failed");
    }
}
static void GLSetAttribute(SdlGLAttr attr, int value)
{
    if (!Sdl.Video.GLSetAttribute(attr, value))
    {
        throw SdlException.FromError("SDL_GL_SetAttribute failed");
    }
}

static bool PollEvent(ref SdlEvent @event)
{
    unsafe
    {
        fixed (SdlEvent* p = &@event)
        {
            return Sdl.Events.PollEvent(p);
        }
    }
}

static bool PushEvent(SdlEvent @event)
{
    unsafe
    {
        return Sdl.Events.PushEvent(&@event);
    }
}

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

SdlWindow window;
try
{
    SetAppMetadata("BRUTAL Rainbow Triangle", "0.0.1", "org.example.brutal-rainbow-triangle");

    Sdl.Init.Initialize(SdlInitFlags.Video);

    GLSetAttribute(SdlGLAttr.ContextMajorVersion, 3);
    GLSetAttribute(SdlGLAttr.ContextMinorVersion, 2);
    GLSetAttribute(SdlGLAttr.ContextProfileMask, Sdl.Constants.GlContextProfileCore);
    GLSetAttribute(SdlGLAttr.Doublebuffer, 1);

    window = Sdl.Video.CreateWindow("BRUTAL Rainbow Triangle", 1280, 720, SdlWindowFlags.Opengl | SdlWindowFlags.Resizable | SdlWindowFlags.InputFocus);
}
catch (SdlException error)
{
    rootLogger.Critical(error);
    return;
}

var glContext = window.GLCreateContext();
if (glContext == nint.Zero)
{
    rootLogger.Critical(SdlException.FromError("SDL_GL_CreateContext failed"));
    return;
}

unsafe
{
    var success = Glad.Initialize(procName =>
    {
        if (procName == null)
            return 0;

        var name = Marshal.PtrToStringUTF8((nint)procName);
        if (name is null)
            return 0;

        return (nint)Sdl.PInvoke.GLGetProcAddress(name);
    });
    if (!success)
    {
        rootLogger.Critical("Failed to initialize OpenGL using Glad.");
        return;
    }
}

if (!Sdl.Video.GLSetSwapInterval(1))
{
    rootLogger.Critical(SdlException.FromError("SDL_GL_SetSwapInterval failed"));
    return;
}

window.GetSizeInPixels(out int w, out int h);
UpdateViewport(new int2(w, h));// init viewport

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

var @event = new SdlEvent();
var ShouldClose = false;
while (!ShouldClose)
{
    while (PollEvent(ref @event))
    {
        switch (@event.type)
        {
            case SdlEventType.Quit:
            case SdlEventType.WindowCloseRequested:
                ShouldClose = true;
                break;
            case SdlEventType.WindowResized:
                UpdateViewport(new int2(@event.window.data1, @event.window.data2));
                break;
            case SdlEventType.KeyDown:
                switch (@event.key.key)
                {
                    default:
                        rootLogger.Debug($"Key {@event.key.key} down");
                        break;
                }
                break;
            case SdlEventType.KeyUp:
                switch (@event.key.key)
                {
                    case SdlKeycode.Escape:
                        PushEvent(new SdlEvent { type = SdlEventType.Quit, quit = new SdlQuitEvent { type = SdlEventType.Quit } });
                        break;
                    default:
                        rootLogger.Debug($"Key {@event.key.key} up");
                        break;
                }
                break;
            default:
                break;
        }
    }

    OpenGl.ClearColor(clearColor);
    OpenGl.Clear(GlClearBit.Color);

    program.Use();
    program.Uniform(0, projection);
    vao.Bind();
    OpenGl.Draw(GlTopology.Triangles, 3, 1, 0, 0);



    window.GLSwap();
}

program.Dispose();
window.Dispose();

Sdl.Init.Quit();
