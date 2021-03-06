using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Tools.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using FluentAssertions;
using static System.Environment;

namespace NetcoreCliFsc.DotNet.Tests
{
    public class CommonScenario : TestBase
    {
        private static IEnumerable<string> NugetConfigSources
        {
            get 
            { 
                yield return "https://api.nuget.org/v3/index.json";
                var pkgsDir = Path.Combine(RepoRoot, "test", "packagesToTest");
                if (Directory.Exists(pkgsDir))
                    yield return pkgsDir;
            }
        }

        private static string NugetPackagesDir
        {
            get { return Path.Combine(RepoRoot, "test", "packages"); }
        }

        private static string RestoreSourcesArgs(IEnumerable<string> sources)
        {
            return string.Join(" ", sources.Select(x => $"--source \"{x}\""));
        }

        private static string RestoreProps()
        {
            var props = new Dictionary<string,string>() 
            {
                { "FSharpNETSdkVersion", GetEnvironmentVariable("TEST_SUITE_FSHARP_NET_SDK_PKG_VERSION")},
                { "MicrosoftFSharpCorenetcoreVersion", GetEnvironmentVariable("TEST_SUITE_MS_FSHARP_CORE_PKG_VERSION")},
            };

            return string.Join(" ", props.Where(kv => kv.Value != null).Select(kv => $"/p:{kv.Key}={kv.Value}") );
        }

        private static string RestoreDefaultArgs
        {
            get { return $"--no-cache {LogArgs} --packages \"{NugetPackagesDir}\""; }
        }

        private static string LogArgs => "-v n";

        [Fact]
        public void TestAppWithArgs()
        {
            var rootPath = Temp.CreateDirectory().Path;

            TestAssets.CopyDirTo("TestAppWithArgs", rootPath);
            TestAssets.CopyDirTo("TestSuiteProps", rootPath);

            Func<string,TestCommand> test = name => new TestCommand(name) { WorkingDirectory = rootPath };

            test("dotnet")
                .Execute($"restore {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()}")
                .Should().Pass();

            test("dotnet")
                .Execute($"build {LogArgs}")
                .Should().Pass();

            test("dotnet")
                .Execute($"run {LogArgs}")
                .Should().Pass();
        }

        [Fact]
        public void TestLibrary()
        {
            var rootPath = Temp.CreateDirectory().Path;

            TestAssets.CopyDirTo("TestLibrary", rootPath);
            TestAssets.CopyDirTo("TestSuiteProps", rootPath);

            Func<string,TestCommand> test = name => new TestCommand(name) { WorkingDirectory = rootPath };

            test("dotnet")
                .Execute($"restore {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()}")
                .Should().Pass();

            test("dotnet")
                .Execute($"build {LogArgs}")
                .Should().Pass();
        }

        [Fact]
        public void TestApp()
        {
            var rootPath = Temp.CreateDirectory().Path;

            foreach (var a in new[] { "TestLibrary", "TestApp" })
            {
                var projDir = Path.Combine(rootPath, a);
                TestAssets.CopyDirTo(a, projDir);
                TestAssets.CopyDirTo("TestSuiteProps", projDir);
            }

            var appDir = Path.Combine(rootPath, "TestApp");

            Func<string,TestCommand> test = name => new TestCommand(name) { WorkingDirectory = appDir };

            test("dotnet")
                .Execute($"restore {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()}")
                .Should().Pass();

            test("dotnet")
                .Execute($"build {LogArgs}")
                .Should().Pass();

            test("dotnet")
                .Execute($"run {LogArgs}")
                .Should().Pass();
        }

        [Fact]
        public void TestPathWithBlank()
        {
            var rootPath = Path.Combine(Temp.CreateDirectory().Path, "path with blank");

            TestAssets.CopyDirTo("TestLibrary", rootPath);
            TestAssets.CopyDirTo("TestSuiteProps", rootPath);

            Func<string,TestCommand> test = name => new TestCommand(name) { WorkingDirectory = rootPath };

            test("dotnet")
                .Execute($"restore {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()}")
                .Should().Pass();

            test("dotnet")
                .Execute($"build {LogArgs}")
                .Should().Pass();
        }

        private string GetCurrentRID()
        {
            var rootPath = Temp.CreateDirectory().Path;

            Func<string,TestCommand> test = n => new TestCommand(n) { WorkingDirectory = rootPath };
            
            var result = test("dotnet").ExecuteWithCapturedOutput($"--info");

            result.Should().Pass();

            var dotnetInfo = result.StdOut;

            string rid = 
                dotnetInfo
                .Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.StartsWith("RID:"))
                .Select(s => s.Replace("RID:", "").Trim())
                .FirstOrDefault();

            return rid;
        }

        private void CreateNoopExe(string intoDir, string name, bool fail = false)
        {
            var rootPath = Temp.CreateDirectory().Path;

            TestAssets.CopyDirTo("Noop", rootPath);

            Func<string,TestCommand> test = n => new TestCommand(n) { WorkingDirectory = rootPath };

            string rid = GetCurrentRID();
            string msbuildArgs = $"/p:AssemblyName={name} " + (fail? "/p:Fail=true" : "");

            test("dotnet")
                .Execute($"restore -r {rid} {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()} {msbuildArgs}")
                .Should().Pass();
            
            test("dotnet")
                .Execute($"publish -r {rid} -o \"{intoDir}\" {msbuildArgs}")
                .Should().Pass();
        }

        [Fact]
        public void DifferentDotnetInPATH()
        {
            var rootPath = Temp.CreateDirectory().Path;

            var fakeDotnetDir = Path.Combine(rootPath, "dotnetsdk");

            Directory.CreateDirectory(fakeDotnetDir);
            CreateNoopExe(fakeDotnetDir, "dotnet", fail : true);

            var appDir = Path.Combine(rootPath, "TestApp");

            TestAssets.CopyDirTo("TestLibrary", appDir);
            TestAssets.CopyDirTo("TestSuiteProps", appDir);

            Func<string,TestCommand> test = name => new TestCommand(name) { WorkingDirectory = appDir };

            test("dotnet")
                .Execute($"restore {RestoreDefaultArgs} {RestoreSourcesArgs(NugetConfigSources)} {RestoreProps()}")
                .Should().Pass();

            var dotnetPath = Microsoft.DotNet.Cli.Utils.Env.GetCommandPath("dotnet");

            var newPATHEnvVar = fakeDotnetDir + Path.PathSeparator + GetEnvironmentVariable("PATH");
            
            test(dotnetPath)
                .WithEnvironmentVariable("PATH", newPATHEnvVar)
                .Execute($"build {LogArgs}")
                .Should().Pass();
        }
    }
}
