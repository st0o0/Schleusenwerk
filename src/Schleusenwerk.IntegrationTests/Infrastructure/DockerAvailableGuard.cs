using System.Diagnostics;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public static class DockerAvailableGuard
{
    private static readonly Lazy<bool> IsAvailable = new(Check);

    public static void SkipIfUnavailable()
    {
        if (!IsAvailable.Value)
        {
            Assert.Skip("Docker is not available — skipping");
        }
    }

    private static bool Check()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
