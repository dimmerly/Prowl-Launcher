using System.Diagnostics;
using System.Reflection;

using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

using Prowl.Aperture;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher.Test.E2E;

/// <summary>Drives the real rendered Paper tree through its pointer/key input pipeline.</summary>
sealed class UiDriver(Launcher launcher, string diagnostics)
{
    private readonly List<TaskCompletionSource> _frames = [];
    private GameWindow _window = null!;
    private Paper _paper = null!;
    private bool _started;
    internal Exception? Failure { get; private set; }
    internal bool Completed { get; private set; }
    internal bool ExpectsClose { get; private set; }
    internal void ExpectClose() => ExpectsClose = true;
    internal Paper Paper => _paper;
    internal Launcher Launcher => launcher;

    internal void Attach(Func<UiDriver, Task> scenario) => _ = Task.Run(async () =>
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (GetField<GameWindow?>(launcher, "_window") is not {} window)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The native window was not created.");
            }
            await Task.Delay(10);
        }
        _window = GetField<GameWindow>(launcher, "_window");
        _window.RenderFrame += _ => OnFrame(scenario);
    });

    private void OnFrame(Func<UiDriver, Task> scenario)
    {
        if (GetField<Paper?>(launcher, "_paper") is not {} paper || paper.ElementCount == 0)
        {
            return;
        }
        _paper = paper;
        _window.IsVisible = false;
        // Hidden windows are normally throttled to 5 Hz. Keep automation deterministic and responsive.
        SetField(launcher, "_windowResizing", true);
        TaskCompletionSource[] pending = _frames.ToArray();
        _frames.Clear();
        foreach (TaskCompletionSource frame in pending)
        {
            frame.TrySetResult();
        }
        if (_started)
        {
            return;
        }
        _started = true;
        _ = RunScenario(scenario);
    }

    private async Task RunScenario(Func<UiDriver, Task> scenario)
    {
        try
        {
            await scenario(this);
            Completed = true;
        }
        catch (Exception error)
        {
            Failure = error;
            Directory.CreateDirectory(diagnostics);
            await File.WriteAllTextAsync(Path.Combine(diagnostics, "ui-tree.txt"), DumpTree());
            try { SaveScreenshot(Path.Combine(diagnostics, "failure.png")); }
            catch (Exception capture) { Console.Error.WriteLine("Screenshot failed: " + capture.Message); }
        }
        finally { Close(); }
    }

    internal async Task Frame(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            TaskCompletionSource next = new( TaskCreationOptions.RunContinuationsAsynchronously );
            _frames.Add(next);
            await next.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    internal IEnumerable<ElementHandle> Nodes()
    {
        Stack<ElementHandle> pending = new();
        pending.Push(_paper.RootElement);
        while (pending.TryPop(out ElementHandle node))
        {
            if (!node.IsValid)
            {
                continue;
            }
            yield return node;
            foreach (int child in node.Data.ChildIndices.AsEnumerable().Reverse())
            {
                pending.Push(new ElementHandle(_paper, child));
            }
        }
    }

    internal bool HasText(string text) => Nodes().Any(n => Labels(n).Any(label => label.Contains(text, StringComparison.Ordinal)));
    internal ElementHandle Text(string text)
    {
        ElementHandle[] matches = Nodes().Where(n => Labels(n).Contains(text)).ToArray();
        ElementHandle interactive = matches.LastOrDefault(n => InteractiveAncestor(n).IsValid);
        return interactive.IsValid ? interactive : matches.LastOrDefault();
    }

    private static ElementHandle InteractiveAncestor(ElementHandle node)
    {
        while (node.IsValid && node.Data.OnClick == null && node.Data.OnPress == null && node.Data.OnTextInput == null)
        {
            node = node.GetParentHandle();
        }
        return node;
    }

    // Origami buttons paint their labels through render snapshots rather than Paragraph.
    // Read those snapshots for selection; all actions still travel through Paper input.
    private IEnumerable<string> Labels(ElementHandle node)
    {
        if (node.Data.Paragraph is {} paragraph)
        {
            yield return paragraph;
        }
        if (node.Data.OnTextInput != null)
        {
            ElementBuilder.TextInputState state = _paper.GetElementStorage<ElementBuilder.TextInputState>(node, "TextInputState", default);
            if (state.Value is {} text)
            {
                yield return text;
            }
        }
        object data = node.Data;
        foreach (string name in new[]
            {
                "_renderCommands",
                "_foregroundRenderCommands"
            })
        {
            if (data.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(data) is not System.Collections.IEnumerable commands)
            {
                continue;
            }
            foreach (object command in commands)
            {
                object? action = command.GetType().GetField("RenderAction")?.GetValue(command)
                                 ?? command.GetType().GetProperty("RenderAction")?.GetValue(command);
                if (action is Delegate render)
                {
                    foreach (string label in SnapshotLabels(render.Target, 0))
                    {
                        yield return label;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> SnapshotLabels(object? value, int depth)
    {
        if (value == null || depth > 2)
        {
            yield break;
        }
        foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? child = field.GetValue(value);
            if (child is string text && field.Name is "Label" or "Text" or "Placeholder" or "_placeholder" or "value")
            {
                yield return text;
            }
            else if (child != null && (field.FieldType.Name.Contains("Snapshot") || field.FieldType.Name.Contains("DisplayClass") || field.FieldType.Name == "TextInputSettings"))
            {
                foreach (string label in SnapshotLabels(child, depth + 1))
                {
                    yield return label;
                }
            }
        }
    }

    internal async Task Wait(Func<bool> condition, string description, double seconds = 10)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed.TotalSeconds > seconds)
            {
                throw new TimeoutException(description + "\n" + DumpTree());
            }
            await Frame();
        }
    }

    internal async Task ClickText(string text)
    {
        await Wait(() => Text(text).IsValid, "Expected visible control: " + text);
        ElementHandle node = Text(text);
        while (node.IsValid && node.Data.OnClick == null && node.Data.OnPress == null && node.Data.OnTextInput == null)
        {
            node = node.GetParentHandle();
        }
        Check(node.IsValid, "No interactive ancestor for " + text);
        await Click(node);
    }

    internal async Task Click(ElementHandle node, PaperMouseBtn button = PaperMouseBtn.Left)
    {
        ref ElementData data = ref node.Data;
        float x = data.X + data.LayoutWidth / 2, y = data.Y + data.LayoutHeight / 2;
        await ClickAt(x, y, button);
    }

    internal async Task ClickAt(float x, float y, PaperMouseBtn button = PaperMouseBtn.Left)
    {
        _paper.SetPointerState(PaperMouseBtn.Unknown, x, y, false, true);
        await Frame();
        _paper.SetPointerState(button, x, y, true, false);
        await Frame();
        _paper.SetPointerState(button, x, y, false, false);
        await Frame(2);
    }

    internal Task Navigate(string page) => ClickAt(36, page switch
    {
        "Projects" => 107, "Versions" => 159, "Samples" => 211, "Settings" => _paper.Height - 34,
        _ => throw new ArgumentException("Unknown sidebar page: " + page)
    });

    internal async Task ReplaceText(string current, string value)
    {
        await Wait(() => Text(current).IsValid, "Expected text field containing: " + current);
        await Click(Text(current));
        await KeyChord(PaperKey.LeftControl, PaperKey.A);
        _paper.AddInputCharacter(value);
        await Frame(2);
    }

    internal async Task FillBelowLabel(string label, string value)
    {
        await Wait(() => Text(label).IsValid, "Expected field label: " + label);
        ElementData data = Text(label).Data;
        await ClickAt(data.X + data.LayoutWidth / 2, data.Y + data.LayoutHeight + 25);
        await KeyChord(PaperKey.LeftControl, PaperKey.A);
        _paper.AddInputCharacter(value);
        await Frame(2);
    }

    internal async Task FillBesideLabel(string label, string value)
    {
        await Wait(() => Text(label).IsValid, "Expected field label: " + label);
        ElementData data = Text(label).Data;
        await ClickAt(data.X + data.LayoutWidth + 50, data.Y + data.LayoutHeight / 2);
        await KeyChord(PaperKey.LeftControl, PaperKey.A);
        _paper.AddInputCharacter(value);
        await Frame(2);
    }

    internal async Task KeyChord(PaperKey modifier, PaperKey key)
    {
        _paper.SetKeyState(modifier, true);
        _paper.SetKeyState(key, true);
        await Frame();
        _paper.SetKeyState(key, false);
        _paper.SetKeyState(modifier, false);
        await Frame();
    }

    internal async Task Scroll(float amount = -8)
    {
        _paper.SetPointerState(PaperMouseBtn.Unknown, _paper.Width - 200, _paper.Height / 2, false, true);
        _paper.SetPointerWheel(amount);
        await Frame(20);
    }

    internal async Task HoverText(string text)
    {
        await Wait(() => Text(text).IsValid, "Expected hover target: " + text);
        ElementData data = Text(text).Data;
        _paper.SetPointerState(PaperMouseBtn.Unknown, data.X + 8, data.Y + 8, false, true);
        await Frame(3);
    }

    internal string DumpTree() => string.Join("\n", Nodes()
        .Select(n => $"{n.Data.ID} [{n.Data.X:F0},{n.Data.Y:F0} {n.Data.LayoutWidth:F0}x{n.Data.LayoutHeight:F0}] {string.Join(" | ", Labels(n))} Interactive={n.Data.OnClick != null || n.Data.OnPress != null || n.Data.OnTextInput != null}"));

    internal static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    internal void Close()
    {
        SetField(launcher, "_forceClose", true);
        _window.Close();
    }

    private void SaveScreenshot(string path)
    {
        int width = _window.FramebufferSize.X, height = _window.FramebufferSize.Y;
        byte[] pixels = new byte[width * height * 4], flipped = new byte[width * height * 4];
        OpenTK.Graphics.OpenGL4.GL.ReadPixels(0, 0, width, height,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, pixels);
        for (int y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
        }
        using Image image = Aperture.Image.FromPixels(flipped, width, height, Aperture.PixelFormat.Rgba8);
        image.Save(path);
    }

    internal static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    internal static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
