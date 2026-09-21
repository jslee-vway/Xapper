using System.Reflection;

namespace Xapper.Tests;

/// <summary>
/// .NET 런타임이 DOTNET_STARTUP_HOOKS 에서 찾는 타입의 모양을 고정한다.
/// 이름·네임스페이스 없음·시그니처는 런타임이 정한 규약이라, 여기서 어긋나면 런치가 조용히 죽는다.
/// </summary>
public class StartupHookShapeTests
{
    [Fact]
    public void StartupHook_HasTheShapeTheRuntimeRequires()
    {
        var inspector = typeof(Xapper.Inspector.EntryPoint).Assembly;

        var hook = inspector.GetType("StartupHook", throwOnError: false);

        Assert.NotNull(hook);
        Assert.Null(hook.Namespace);

        var initialize = hook.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(initialize);
        Assert.Equal(typeof(void), initialize.ReturnType);
        Assert.Empty(initialize.GetParameters());
    }
}
