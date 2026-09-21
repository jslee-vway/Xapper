using Xapper.Inspector;

/// <summary>
/// <c>DOTNET_STARTUP_HOOKS</c> 가 부르는 진입점. 런타임이 대상 앱의 <c>Main</c> 보다 먼저 이 메서드를 부른다.
/// 타입 이름, 네임스페이스가 없다는 것, 시그니처는 모두 런타임이 정한 규약이라 바꿀 수 없다.
///
/// 본문은 주입 경로와 같은 초기화를 부르는 것뿐이다 — 어셈블리 리졸버 등록과 파이프 서버 기동이 이미 거기 있다.
/// 그 초기화는 어떤 예외도 밖으로 내지 않으므로(결함 2 원칙), 훅이 실패해도 대상 앱은 그대로 기동한다.
/// </summary>
internal static class StartupHook
{
    /// <summary>앱의 진입점보다 먼저 호출되어 Inspector 를 기동합니다.</summary>
    public static void Initialize()
    {
        EntryPoint.Initialize("startup-hook");
    }
}
