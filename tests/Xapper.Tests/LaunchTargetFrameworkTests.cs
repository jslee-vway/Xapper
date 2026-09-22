using Xapper.Injector;

namespace Xapper.Tests;

/// <summary>
/// 아직 띄우지 않은 앱의 런타임 세대를 runtimeconfig.json 에서 읽는 규칙을 검증한다.
/// 주입 경로는 실행 중인 프로세스를 검사하지만 런치 시점에는 프로세스가 없으므로 이 파일이 유일한 단서다.
/// </summary>
public class LaunchTargetFrameworkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xapper-tfm-" + Guid.NewGuid().ToString("N"));

    public LaunchTargetFrameworkTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private string WriteApp(string runtimeConfigJson)
    {
        var exe = Path.Combine(_directory, "App.exe");
        File.WriteAllText(exe, "");
        File.WriteAllText(Path.Combine(_directory, "App.runtimeconfig.json"), runtimeConfigJson);
        return exe;
    }

    [Fact]
    public void MajorVersionOf_ReadsTheTfmField()
    {
        var exe = WriteApp("""{"runtimeOptions":{"tfm":"net9.0","frameworks":[{"name":"Microsoft.WindowsDesktop.App","version":"9.0.0"}]}}""");

        Assert.Equal(9, LaunchTargetFramework.MajorVersionOf(exe));
    }

    [Fact]
    public void MajorVersionOf_FallsBackToTheFrameworkVersions()
    {
        var exe = WriteApp("""{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"8.0.0"}]}}""");

        Assert.Equal(8, LaunchTargetFramework.MajorVersionOf(exe));
    }

    [Fact]
    public void MajorVersionOf_ReadsTheOlderSingularFramework()
    {
        var exe = WriteApp("""{"runtimeOptions":{"framework":{"name":"Microsoft.WindowsDesktop.App","version":"6.0.0"}}}""");

        Assert.Equal(6, LaunchTargetFramework.MajorVersionOf(exe));
    }

    [Fact]
    public void MajorVersionOf_WithoutAConfigFileIsUnknown()
    {
        var exe = Path.Combine(_directory, "NoConfig.exe");
        File.WriteAllText(exe, "");

        Assert.Null(LaunchTargetFramework.MajorVersionOf(exe));
    }

    [Fact]
    public void MajorVersionOf_WithBrokenJsonIsUnknown()
    {
        var exe = WriteApp("{ this is not json");

        Assert.Null(LaunchTargetFramework.MajorVersionOf(exe));
    }

    [Fact]
    public void MajorVersionOf_WithNothingUsableIsUnknown()
    {
        var exe = WriteApp("""{"runtimeOptions":{"configProperties":{}}}""");

        Assert.Null(LaunchTargetFramework.MajorVersionOf(exe));
    }
}
