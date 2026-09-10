namespace Xapper.Protocol;

/// <summary>
/// 실제 마우스 입력이 있어야만 수행할 수 있는 요청이라는 표시.
/// 주입된 쪽이 판단하고 도구 쪽이 그 판단을 알아채야 하므로, 양쪽이 같은 문자열을 참조하도록 한곳에 둔다.
/// </summary>
public static class RealInputRequired
{
    /// <summary>거절 사유가 "실제 입력이 필요하다"임을 알리는 표시.</summary>
    public const string Marker = "[real-input-required]";
}
