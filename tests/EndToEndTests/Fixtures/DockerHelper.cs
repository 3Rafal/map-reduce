using System.Diagnostics;

namespace EndToEndTests.Fixtures
{
    public static class DockerHelper
    {
        public static async Task<string> RunCommandAsync(string arguments, bool throwOnError = true)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start docker process.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && throwOnError)
            {
                throw new InvalidOperationException($"Docker command failed: docker {arguments}/nSTDOUT: {stdout}/nSTDERR: {stderr}");
            }

            return stdout.Trim();
        }
    }
}