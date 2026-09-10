using System.Windows;
using System.Windows.Media;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 비주얼 자식을 열거하되 순회할 수 없는 자식은 건너뛰는 헬퍼.
/// 자식 슬롯을 직접 관리하는 컨트롤은 자식이 있다고 보고하면서도 아직 채워지지 않은 슬롯에 대해
/// 아무것도 돌려주지 않을 수 있고, 그것을 그대로 따라 들어가면 트리 탐색 전체가 중단된다.
/// 검색과 스냅샷이 같은 규칙으로 순회하도록 이 판단을 한곳에 모은다.
/// </summary>
internal static class VisualChildren
{
    #region Public Methods

    /// <summary>
    /// 지정된 요소의 자식 중 순회할 수 있는 것만 반환합니다.
    /// </summary>
    /// <param name="parent">자식을 열거할 요소.</param>
    /// <param name="depth">루트로부터의 깊이. 건너뛴 자식의 위치를 알리는 데 사용.</param>
    /// <param name="skipped">건너뛴 자식의 설명을 기록할 목록.</param>
    /// <returns>순회할 수 있는 자식 목록. 건너뛴 자식은 포함되지 않음.</returns>
    public static List<DependencyObject> Of(DependencyObject parent, int depth, List<string> skipped)
    {
        var children = new List<DependencyObject>();

        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(parent);
        }
        catch (Exception ex)
        {
            skipped.Add(Describe(parent, depth, index: null, reason: ex.GetType().Name));
            return children;
        }

        for (var index = 0; index < count; index++)
        {
            DependencyObject? child;
            try
            {
                child = VisualTreeHelper.GetChild(parent, index);
            }
            catch (Exception ex)
            {
                skipped.Add(Describe(parent, depth, index, ex.GetType().Name));
                continue;
            }

            if (child is null)
            {
                skipped.Add(Describe(parent, depth, index, "child slot was empty"));
                continue;
            }

            children.Add(child);
        }

        return children;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 건너뛴 자식의 위치와 사유를 한 줄로 설명합니다. 호출자가 원인을 좁힐 수 있도록 부모의 타입과 이름을 함께 남깁니다.
    /// </summary>
    private static string Describe(DependencyObject parent, int depth, int? index, string reason)
    {
        var name = (parent as FrameworkElement)?.Name;
        var namePart = string.IsNullOrEmpty(name) ? "" : $" name=\"{name}\"";
        var indexPart = index.HasValue ? $" child[{index}]" : "";
        return $"{parent.GetType().Name}{namePart} depth={depth}{indexPart}: {reason}";
    }

    #endregion
}
