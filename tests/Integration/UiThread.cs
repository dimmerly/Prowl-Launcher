namespace Prowl.Launcher.Test;

/// <summary>Runs async code the way the launcher does: awaits resume on this thread, which owns the store.</summary>
internal static class UiThread
{
    public static void Run(Func<Task> body)
    {
        using UiContext context = new();
        Task task = body();
        while (!task.IsCompleted)
        {
            context.Pump();
            Thread.Sleep(1);
        }

        context.Pump();
        task.GetAwaiter().GetResult();
    }
}
