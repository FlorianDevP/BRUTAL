using System.Reflection;
using Brutal.Enums;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImPlotApi;
using Brutal.Logging;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Brutal.Strings;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.Extensions.DependencyModel;

namespace glfw_opengl;

public sealed record DependencyNode(string Name, string Version)
{
    public List<DependencyNode> Children { get; } = [];

    public String8 Label { get; } = new($"{Name} {Version}##node");

    public bool HasChildren => Children.Count != 0;
}

public sealed class RuntimeLibrary
{
    public readonly string Name;
    public readonly string Version;

    private readonly Microsoft.Extensions.DependencyModel.RuntimeLibrary library;
    public readonly String8 Label;
    public readonly String8[] RuntimeAssemblyGroups;

    public RuntimeLibrary(Microsoft.Extensions.DependencyModel.RuntimeLibrary library)
    {
        this.library = library;
        Name = library.Name;
        Version = library.Version;
        Label = new String8($"{library.Name} {library.Version}##runtime");

        RuntimeAssemblyGroups = [.. library.RuntimeAssemblyGroups.SelectMany(group => group.AssetPaths).Select(asset => new String8(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(asset)))).Distinct()];
    }
}

public sealed record Texture(GlTexture GL, float2 Size) : IDisposable
{
    public uint Id => GL.Id;

    public static Texture Create(float2 size)
    {
        var gl = default(GlTexture);
        OpenGl.GenTextures(new Span<GlTexture>(ref gl));
        return new Texture(gl, size);
    }

    public void Dispose()
    {
        var gl = GL;// copy handle to a local (stack)
        OpenGl.DeleteTextures(new ReadOnlySpan<GlTexture>(in gl));
    }
}

internal sealed class App : IDisposable
{
    /// <summary>
    /// App root log category
    /// </summary>
    public static readonly LogCategory Log = LogCategory.Make("App");
    public static readonly Brutal.ImGuiApi.Abstractions.ConsoleWindow Console = new();

    private readonly GlfwWindow window;
    private readonly ImGuiContextPtr imguiContext;
    private readonly ImPlotContextPtr implotContext;
    private readonly Brutal.ImGuiApi.Backends.GlfwBackend imguiPlatform;
    private readonly Brutal.ImGuiApi.Backends.OpenGlBackend imguiRenderer;

    private float4x4 projection;
    private readonly GlProgram triangleProgram;
    private readonly GlVertexArray triangleVAO;
    private readonly GlBuffer triangleVBO;
    private readonly Texture logoTexture;

    // imgui states
    private bool showDemoWindow = false;
    private bool showPlot = false;
    private bool showBrutalLogo = true;
    private bool showDecompilerWindow = true;
    private bool showPackages = false;
    private bool showImGuiAbout = false;
    // decompilation analyzer
    private record struct Dockspace
    {
        public ImGuiID id;
        public ImGuiID centerId;
        public ImGuiID leftId;
    }
    private Dockspace decompAnalyzerDockspace = new();
    private List<Type> OpenDecompiledTypes = [];
    // debug info
    private readonly DependencyNode? ExecutablePackage;
    private readonly String8[] CompileLibraries;
    private readonly RuntimeLibrary[] RuntimeLibrarys;
    private readonly CompilationAnalysis Analyser;
    // data for ImPlot demo
    private readonly float[] xs = new float[200];
    private readonly float[] ys = new float[200];

    /// <summary>
    /// constant triangle vertice f3 position + f3 color
    /// </summary>
    private static readonly float3[] vertices = [
        new(-0.5f, -0.5f, 0f), new(1f, 0f, 0f),
        new(0.5f, -0.5f, 0f), new(0f, 1f, 0f),
        new(0f,  0.5f, 0f), new(0f, 0f, 1f),
    ];

    public static string ReadTextResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName) ?? throw new FileLoadException("Failed to get Manifest Resource Stream");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static byte[] ReadResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using Stream resourceStream = assembly.GetManifestResourceStream(fullName) ?? throw new FileLoadException("Failed to get Manifest Resource Stream");
        var buffer = new byte[resourceStream.Length];
        resourceStream.ReadExactly(buffer, 0, buffer.Length);
        return buffer;
    }

    /// Helper to display a little (?) mark which shows a tooltip when hovered.
    /// In your own code you may want to display an actual icon if you are using a merged icon fonts (see docs/FONTS.md)
    public static void HelpMarker(ImString description)
    {
        ImGui.TextDisabled("(?)"u8);
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.Text(description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// ImGui.Text with popup to copy it's content
    public static void Text(ImString text, ImString value = default)
    {
        ImGui.Text(text);
        if (ImGui.BeginPopupContextItem(text))
        {
            if (ImGui.Button("Copy"u8))
            {
                ImGui.SetClipboardText(value.IsSet ? value : text);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public static void TextWrapped(ImString text, ImString value = default)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.TextWrapped(text);
        if (ImGui.IsItemHovered())
        {
            drawList.ChannelsSetCurrent(0);// back channel: the highlight
            var hoverColor = ImGui.GetColorU32(ImGuiCol.HeaderHovered);
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            max.X = min.X + rowWidth;
            drawList.AddRectFilled(min, max, hoverColor, ImGui.GetStyle().FrameRounding);
        }
        drawList.ChannelsMerge();

        // copy popup
        if (ImGui.BeginPopupContextItem(text))
        {
            if (ImGui.Button("Copy"u8))
            {
                ImGui.SetClipboardText(value.IsSet ? value : text);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public App()
    {
        Log.Debug(Assembly.GetExecutingAssembly().Location);
        Log.Debug(string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames()));
        // create debug information
        var context = DependencyContext.Load(Assembly.GetExecutingAssembly()) ?? DependencyContext.Default;
        var compileLibraries = context.CompileLibraries.Select(lib => lib.Name);
        CompileLibraries = [.. compileLibraries.Select(name => new String8(name))];
        RuntimeLibrarys = [.. context.RuntimeLibraries.Where(lib => compileLibraries.Contains(lib.Name)).Select(lib => new RuntimeLibrary(lib))];
        ExecutablePackage = context != null ? BuildTree(context) : null;
        Analyser = new CompilationAnalysis();

        // create window with hints to use OpenGL 4.6
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 6);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
        Glfw.WindowHint(GlfwWindowHint.Doublebuffer, Glfw.Constants.GLFW_TRUE);
        Glfw.WindowHint(GlfwWindowHint.ScaleToMonitor, Glfw.Constants.GLFW_TRUE);
        if (Brutal.Platform.IsMacOS)
            Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, 1);
        window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = "BRUTAL Rainbow Triangle",
            Size = new int2(1280, 720),
        });
        window.MakeContextCurrent();
        Glfw.SwapInterval(1);

        // initalize OpenGL newer than 1.0 using Glad
        unsafe
        {
            var success = Glad.Initialize(Glfw.PInvoke.GetProcAddress);
            if (!success)
                throw new InvalidOperationException("Failed to load OpenGL functions with GLAD");
        }

        // setup ImGui and ImPlot context and backends
        imguiContext = ImGui.CreateContext();
        implotContext = ImPlot.CreateContext();
        ImPlot.SetImGuiContext(imguiContext);
        imguiPlatform = new Brutal.ImGuiApi.Backends.GlfwBackend(window, Brutal.ImGuiApi.Backends.GlfwBackend.ClientApi.OpenGL);
        imguiRenderer = new Brutal.ImGuiApi.Backends.OpenGlBackend();
        // set config flags
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        ImGui.LoadIniSettingsFromDisk(Path.Join(AppContext.BaseDirectory, "imgui.ini"));
        // ImGui.LoadIniSettingsFromDisk(Path.Join(Environment.ProcessPath, "imgui.ini"));

        // keyboard key callback to exit using escape
        window.OnKey += (win, key, _, action, _) =>
        {
            if (key == GlfwKey.Escape)
                window.ShouldClose = true;
        };

        // set viewport clear color
        OpenGl.ClearColor(float4.Rgb(0.2f, 0.3f, 0.3f));

        // stride of float3
        var float3Size = float3.ComponentSize * float3.ComponentCount;
        // create vertex array and bind
        triangleVAO = OpenGl.GenVertexArray().Bind();
        // crate vertex buffer and bind as array buffer
        triangleVBO = OpenGl.GenBuffer().Bind(GlBindTarget.ArrayBuffer);
        // write array of vertices and colors to buffer
        OpenGl.BufferData(GlBindTarget.ArrayBuffer, vertices, GlBufferUsage.StaticDraw);
        // setup vertices with offset
        OpenGl.VertexAttribute(new GlAttribute(0, 3, OpenGl.GL_FLOAT, GlScaling.Float, float3Size * 2, 0));
        OpenGl.EnableVertexAttribArray(0);
        // setup colors with offset
        OpenGl.VertexAttribute(new GlAttribute(1, 3, OpenGl.GL_FLOAT, GlScaling.Float, float3Size * 2, float3Size));
        OpenGl.EnableVertexAttribArray(1);

        // create vertex shader
        using var vertexShader = OpenGl.CreateShader(GlShaderType.Vertex);
        try
        {
            // load shader
            vertexShader.SetSource(ReadTextResource("shader.vert"));
            // compile shader
            vertexShader.Compile();
        }
        catch (GlShader.CompileException error)
        {
            throw new Exception("Failed to load and compile vertex shader.", error);
        }

        // create fragment shader
        using var fragShader = OpenGl.CreateShader(GlShaderType.Fragment);
        try
        {
            // load shader
            fragShader.SetSource(ReadTextResource("shader.frag"));
            // compile shader
            fragShader.Compile();
        }
        catch (GlShader.CompileException error)
        {
            throw new Exception("Failed to compile fragment shader.", error);
        }

        // create shader program, add both shaders to it and link into a program
        try
        {
            triangleProgram = OpenGl.CreateProgram().Attach(vertexShader, fragShader).Link();
        }
        catch (GlProgram.LinkException error)
        {
            throw new Exception("Failed to link shader program.", error);
        }

        // load texture
        using var image = Brutal.StbApi.Stb.LoadFromMemory(ReadResource("LOGO_BRUTAL_WHT.png"));
        // create logo Texture
        logoTexture = Texture.Create(new float2(image.Width, image.Height));
        OpenGl.BindTexture(OpenGl.GL_TEXTURE_2D, logoTexture.Id);
        // Setup filtering parameters for display
        OpenGl.TextureParameterInt(logoTexture.Id, OpenGl.GL_TEXTURE_MIN_FILTER, [OpenGl.GL_LINEAR]);// [] is zero cost for constants, lowered to ReadOnlySpan
        OpenGl.TextureParameterInt(logoTexture.Id, OpenGl.GL_TEXTURE_MAG_FILTER, [OpenGl.GL_LINEAR]);// [] is zero cost for constants, lowered to ReadOnlySpan

        OpenGl.PixelStorei(OpenGl.GL_UNPACK_ROW_LENGTH, 0);
        unsafe
        {
            OpenGl.TexImage2D(OpenGl.GL_TEXTURE_2D, 0, (int)OpenGl.GL_RGBA, image.Width, image.Height, 0, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, image.Data);
        }

        // generate data for ImPlot
        for (var i = 0; i < xs.Length; i++)
        {
            xs[i] = i * 0.05f;
            ys[i] = MathF.Sin(xs[i]);
        }
    }

    public void Dispose()
    {
        // delete vertex array
        var vao = triangleVAO;
        OpenGl.DeleteVertexArrays(new Span<GlVertexArray>(ref vao));

        // free backends before destroying ImGui context
        imguiRenderer.Dispose();
        imguiPlatform.Dispose();
        ImPlot.DestroyContext(implotContext);
        ImGui.DestroyContext(imguiContext);
    }

    public void Run()
    {
        var mainThread = Environment.CurrentManagedThreadId;
        // update when the windows contents was damaged by a resize or other events
        window.OnRefresh += (window) =>
        {
            Log.Debug($"UpdateViewport on {(Environment.CurrentManagedThreadId == mainThread ? "main Thread" : "other thread")}");
            Render();
        };

        // update viewport on framebuffer resize
        window.OnFramebufferSize += (win, size) => UpdateViewport(size);
        UpdateViewport(window.FramebufferSize);// init viewport

        // keyboard key callback
        window.OnKey += (win, key, _, action, _) =>
        {
            switch (key)
            {
                default:
                    Log.Debug($"Key {key} {action}");
                    break;
            }
        };

        // main loop
        while (!window.ShouldClose)
        {
            // check events so glfw can call the callbacks
            Glfw.PollEvents();

            Render();
        }
    }

    public void UpdateViewport(int2 size)
    {
        OpenGl.Viewport(0, 0, size.X, size.Y);
        var aspect = (float)size.X / (float)size.Y;
        projection = float4x4.CreateOrthographicOffCenter(-aspect, aspect, -1f, 1f, -1f, 1f);
        //projection = float4x4.CreateOrthographicOffCenter(-aspect, aspect, 1f, 1f, -1f, 1f);
    }

    private DependencyNode BuildTree(DependencyContext context)
    {
        // one node per library, keyed by name
        var nodes = context.RuntimeLibraries.ToDictionary(
            lib => lib.Name,
            lib => new DependencyNode(lib.Name, lib.Version));

        // wire edges: dependencies become child references
        foreach (var lib in context.RuntimeLibraries)
            foreach (var dep in lib.Dependencies)
                if (nodes.TryGetValue(dep.Name, out var child))
                    nodes[lib.Name].Children.Add(child);

        // root is current executable
        return nodes[Assembly.GetExecutingAssembly().GetName().Name ?? throw new Exception("Name of Executing Assembly missing.")];
    }

    private void DependencyNode(DependencyNode node, bool root = false)
    {
        // build flags
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.NoAutoOpenOnLog | ImGuiTreeNodeFlags.NavLeftJumpsToParent;
        if (!node.HasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf;
        if (root)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        // create tree node with flags
        var open = ImGui.TreeNodeEx(node.Label, flags);

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.Button("Copy"u8))
            {
                ImGui.SetClipboardText($"{node.Name}@{node.Version}");
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (open)
        {
            foreach (var child in node.Children)
                DependencyNode(child);
            ImGui.TreePop();
        }

    }

    private void Render()
    {
        // begin new frame for ImGui drawing
        imguiRenderer.NewFrame();
        imguiPlatform.NewFrame();
        ImGui.NewFrame();
        // Recycle the scratch memory that interpolated ImGui strings ($"...") use, once per frame.
        ImString.SharedStorage.Reset();
        // render all windows, widgets, menubars and more
        ImGuiRender();
        // ImGui end Frame
        ImGui.Render();

        // clear the viewport
        OpenGl.Clear(GlClearBit.Color);

        // setup the program for rendering
        triangleProgram.Use();
        // write the projection matrix to the first shader uniform
        triangleProgram.Uniform(0, projection);
        // bind vertex array
        triangleVAO.Bind();
        // draw three vertex array as Triangles once(one instance)
        OpenGl.Draw(GlTopology.Triangles, 3, 1, 0, 0);

        // imgui draw
        imguiRenderer.RenderDrawData(ImGui.GetDrawData());

        // swap buffer to present viewport to window
        window.SwapBuffers();
    }

    private void ImGuiRender()
    {
        ImGui.DockSpaceOverViewport(flags: ImGuiDockNodeFlags.PassthruCentralNode | ImGuiDockNodeFlags.AutoHideTabBar);

        var viewport = ImGui.GetMainViewport();
        Console.Draw(viewport.Size, viewport.Pos, ImGui.GetIO().DeltaTime);

        ImGui.BeginMainMenuBar();
        if (ImGui.BeginMenu("Demos"u8))
        {
            if (ImGui.MenuItem("ImGui DemoWindow"u8, selected: showDemoWindow))
                showDemoWindow = !showDemoWindow;
            if (ImGui.MenuItem("ImPlot"u8, selected: showPlot))
                showPlot = !showPlot;
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("About"u8))
        {
            if (ImGui.MenuItem("Packages"u8, selected: showPackages))
                showPackages = !showPackages;
            if (ImGui.MenuItem("BRUTAL Logo"u8))
                showBrutalLogo = true;
            ImGui.EndMenu();
        }
        ImGui.EndMainMenuBar();

        DecompilerWindow();

        PackagesWindow();

        BrutalLogoWindow();

        if (showDemoWindow)
            ImGui.ShowDemoWindow(ref showDemoWindow);
        if (showImGuiAbout)
            ImGui.ShowAboutWindow(ref showImGuiAbout);

        if (showPlot)
        {
            ImGui.Begin("Hello ImPlot"u8, ref showPlot);
            if (ImPlot.BeginPlot("Sine Wave"u8, new float2(-1, -1))) // Fill the window.
            {
                // Auto-fit only kicks in on the next frame, so give this single frame its limits up front.
                ImPlot.SetupAxesLimits(0, 10, -1.1, 1.1);
                ImPlot.PlotLine("sin(x)"u8, xs, ys);
                ImPlot.EndPlot();
            }
            ImGui.End();
        }
    }

    void BrutalLogoWindow()
    {
        if (showBrutalLogo)
        {
            // create window "splash screen" window with no decorations
            ImGui.Begin("BRUTAL Logo"u8, ref showBrutalLogo, flags: ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking);
            // click on whole window closes it
            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                showBrutalLogo = false;

            // draw logo image
            ImGui.Image(new ImTextureRef { _TexID = new ImTextureID((nint)logoTexture.GL.Id) }, logoTexture.Size);
            // ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, float2.Zero);
            // if (ImGui.ImageButton("brutal logo"u8, new ImTextureRef { _TexID = new ImTextureID((nint)logoTexture.Id) }, logoSize, bgCol: float4.Rgba(0f, 0f, 0f, 1f), tintCol: float4.One))
            //     showLogo = false;
            // ImGui.PopStyleVar();

            // add centered text
            var text = "Click to close"u8;
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX((ImGui.GetWindowSize().X - textWidth) * 0.5f);
            ImGui.Text(text);

            // center window in viewport after automatic resize cause by image and text content
            var position = ImGui.GetWindowSize() / 2;
            var screenSize = new float2(window.FramebufferSize.X, window.FramebufferSize.Y) / 2;
            ImGui.SetWindowPos(screenSize - position, ImGuiCond.Always);

            ImGui.End();
        }
    }

    void PackagesWindow()
    {
        if (!showPackages)
            return;

        ImGui.SetNextWindowSizeConstraints(new float2(300f, 500f), float2.PositiveInfinity);
        if (!ImGui.Begin("Packages"u8, ref showPackages))
        {
            // Early out if the window is collapsed, as an optimization.
            ImGui.End();
            return;
        }

        HelpMarker("Right click elements to copy."u8);
        ImGui.Spacing();

        Text($"Glfw Version: {Glfw.VersionString}", Glfw.VersionString);
        Text($"ImGui Version: {ImGui.GetVersion()}", ImGui.GetVersion());
        ImGui.SameLine();
        if (ImGui.Button("Details"u8))
            showImGuiAbout = true;

        ImGui.SeparatorText("Compile Libraries"u8);
        if (ImGui.BeginChild("##Compile-Libraries"u8, new float2(0f, 100f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
        {
            foreach (var item in CompileLibraries.Enumerate())
                TextWrapped(item.Value);
        }
        ImGui.EndChild();

        ImGui.SeparatorText("Runtime Librarys"u8);
        if (ImGui.BeginChild("##Runtime-Libraries"u8, new float2(0f, 200f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
        {
            foreach (var library in RuntimeLibrarys)
            {
                if (ImGui.TreeNodeEx(library.Label, ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.NoAutoOpenOnLog | ImGuiTreeNodeFlags.NavLeftJumpsToParent))
                {
                    foreach (var assembly in library.RuntimeAssemblyGroups)
                        TextWrapped(assembly);
                    ImGui.TreePop();
                }
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.Button("Copy"u8))
                    {
                        ImGui.SetClipboardText($"{library.Name}@{library.Version}");
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
        }
        ImGui.EndChild();

        ImGui.SeparatorText("Package Graph"u8);
        if (ExecutablePackage == null)
            ImGui.TextColored(float4.Rgb(1f, 0f, 0f), $"Failed to build dependency graph!");
        else
        {
            if (ImGui.BeginChild("##Package-Graph"u8, new float2(0f, 200f), ImGuiChildFlags.FrameStyle | ImGuiChildFlags.ResizeY))
                DependencyNode(ExecutablePackage, true);
            ImGui.EndChild();
        }

        ImGui.End();
    }

    private void DecompilerWindow()
    {
        if (!showDecompilerWindow)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, float2.Zero);
        ImGui.SetNextWindowSizeConstraints(new float2(600f, 400f), float2.PositiveInfinity);
        if (ImGui.Begin("Decompiler"u8, ref showDecompilerWindow, ImGuiWindowFlags.NoDocking))
        {
            // var size = ImGui.GetContentRegionAvail();
            // ImGui.Internal.DockBuilderSetNodeSize()
        }
        ImGui.PopStyleVar(3);

        var size = ImGui.GetContentRegionAvail();

        decompAnalyzerDockspace.id = ImGui.GetID("DecompilerDockSpace"u8);
        var dockspace = ImGui.Internal.DockBuilderGetNode(decompAnalyzerDockspace.id);
        if (dockspace.IsNull())// recreate only when there is no imgui.ini
        {
            decompAnalyzerDockspace.centerId = ImGui.Internal.DockBuilderAddNode(decompAnalyzerDockspace.id, ImGuiDockNodeFlags.None);
            ImGui.Internal.DockBuilderSetNodeSize(decompAnalyzerDockspace.centerId, size);
            // ImGui.Internal.DockBuilderSplitNode(rootId, ImGuiDir.Right, 0.3f, out ImGuiID dockRightId, out rootId);
            ImGui.Internal.DockBuilderSplitNode(decompAnalyzerDockspace.centerId, ImGuiDir.Left, 0.3f, out decompAnalyzerDockspace.leftId, out decompAnalyzerDockspace.centerId);

            ImGui.Internal.DockBuilderDockWindow("Explorer"u8, decompAnalyzerDockspace.leftId);
            ImGui.Internal.DockBuilderFinish(decompAnalyzerDockspace.id);
        }

        // dock dockspace in window
        ImGui.DockSpace(decompAnalyzerDockspace.id, float2.Zero, ImGuiDockNodeFlags.NoUndocking | ImGuiDockNodeFlags.NoDockingSplit | ImGuiDockNodeFlags.NoDockingOverCentralNode);
        ImGui.End();

        if (ImGui.Begin("Explorer"u8))
        {
            ImGui.Text("Explorer Window"u8);
            foreach (var ns in Analyser.Namespaces)
            {
                if (ImGui.TreeNodeEx(ns.Name, ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.NoAutoOpenOnLog | ImGuiTreeNodeFlags.NavLeftJumpsToParent))
                {
                    foreach (var type in ns.Types)
                    {
                        if (ImGui.TreeNodeEx($"{type.Kind.ToString()} {type.Definition.Name}", ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.NoAutoOpenOnLog | ImGuiTreeNodeFlags.NavLeftJumpsToParent))
                        {
                            // if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            // {
                            //     if (!OpenDecompiledTypes.Contains(type))
                            //         OpenDecompiledTypes.Add(type);
                            // }

                            if (type.Kind == SymbolKind.TypeDefinition)
                            {
                                foreach (var property in type.Definition.Properties)
                                    ImGui.Text(property.Name);
                                foreach (var method in type.Definition.Methods)
                                    ImGui.Text(method.Name);
                            }
                            ImGui.TreePop();
                        }

                        if (ImGui.BeginPopupContextItem())
                        {
                            if (ImGui.Button("Open"u8))
                            {
                                if (!OpenDecompiledTypes.Contains(type))
                                    OpenDecompiledTypes.Add(type);
                                ImGui.CloseCurrentPopup();
                            }
                            if (ImGui.Button("Copy"u8))
                            {
                                ImGui.SetClipboardText(type.Definition.FullTypeName.ToString());
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.EndPopup();
                        }
                    }
                    ImGui.TreePop();
                }
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.Button("Copy"u8))
                    {
                        ImGui.SetClipboardText(ns.Name);
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
        }
        ImGui.End();

        if (0 < OpenDecompiledTypes.Count)
            Log.Debug($"Tabs: {OpenDecompiledTypes.Count}");
        foreach (ref var type in OpenDecompiledTypes.AsSpan())
        {
            ImGui.SetNextWindowDockID(decompAnalyzerDockspace.leftId, ImGuiCond.Once);
            if (ImGui.Begin(type.Definition.Name, ImGuiWindowFlags.NoDecoration))
            {
                Analyser.DecompileType(ref type);
                if (type.Decompiled is not null)
                    ImGui.TextWrapped(type.Decompiled.Source);
            }
            ImGui.End();
        }
    }
}
