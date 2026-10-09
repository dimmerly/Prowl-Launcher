using Prowl.Samples;

if (args.Length < 2 || args[0] != "--run-sample")
{
    Console.Error.WriteLine("Usage: Prowl.SampleHost --run-sample <assembly> [--sample-preview <image>]");
    return 1;
}
try
{
    SampleRunner.Run(args[1], args);
    return Environment.ExitCode;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
