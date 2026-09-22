using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 화면을 식별하는 지문을 만든다. 같은 화면이면 같은 값이, 다른 화면이면 다른 값이 나와야 한다.
///
/// 재료는 (타입, Name, AutomationId) 세 값의 <b>중복 없는 집합</b>이고, 텍스트는 넣지 않는다.
/// 셋 다 실측으로 정한 선택이다. 텍스트를 넣으면 그리드 데이터가 바뀔 때마다 다른 화면이 되고,
/// 개수를 세면 가상화 목록을 스크롤할 때마다 달라진다(측정: 노드 105→149개, 고유 요소는 46개 그대로).
///
/// 호출자는 주 창만 넘겨야 한다. 팝업과 메뉴는 각자 최상위 창에 사는데, WPF 는 한 번 연 컨텍스트 메뉴를
/// 닫아도 트리에서 없애지 않고 IsVisible 도 내리지 않아 지문을 영구히 오염시킨다(측정: 전체 트리로 재면
/// 4종류, 주 창만으로 재면 2종류).
/// </summary>
public static class ScreenSignature
{
    #region Public Methods

    /// <summary>지정한 트리의 구조 지문을 만듭니다. 예외를 던지지 않는다.</summary>
    /// <param name="root">지문을 낼 트리의 뿌리. 주 창을 넘긴다.</param>
    /// <returns>16진수 문자열 지문.</returns>
    public static string Of(DependencyObject root)
    {
        var triples = CollectFrom(root);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", triples)));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>지문에 들어간 고유 요소의 수. 호출자가 화면 규모를 가늠하는 데 쓴다.</summary>
    /// <param name="root">셀 트리의 뿌리.</param>
    public static int CountOf(DependencyObject root)
    {
        return CollectFrom(root).Count;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 트리 전체를 훑어 (타입, Name, AutomationId) 조합을 모읍니다.
    /// 건너뛴 자식을 적어 둘 목록은 순회 한 번에 하나만 만들어 아래로 넘긴다 — 노드마다 새로 만들면
    /// 읽지도 않을 목록을 화면 크기만큼 할당하게 된다.
    /// </summary>
    private static SortedSet<string> CollectFrom(DependencyObject root)
    {
        var triples = new SortedSet<string>(StringComparer.Ordinal);
        Collect(root, triples, skipped: new List<string>());
        return triples;
    }

    /// <summary>트리를 훑어 (타입, Name, AutomationId) 조합을 모읍니다. 읽을 수 없는 노드는 건너뜁니다.</summary>
    private static void Collect(DependencyObject node, SortedSet<string> triples, List<string> skipped)
    {
        triples.Add($"{node.GetType().Name}|{NameOf(node)}|{IdOf(node)}");

        foreach (var child in VisualChildren.Of(node, depth: 0, skipped))
            Collect(child, triples, skipped);
    }

    /// <summary>FrameworkElement.Name. 읽을 수 없으면 빈 문자열.</summary>
    private static string NameOf(DependencyObject node)
    {
        try
        {
            return (node as FrameworkElement)?.Name ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>AutomationId. 읽을 수 없으면 빈 문자열.</summary>
    private static string IdOf(DependencyObject node)
    {
        try
        {
            return AutomationProperties.GetAutomationId(node) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    #endregion
}
