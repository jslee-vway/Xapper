using Xapper.Protocol;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 스냅샷 트리에서 셀렉터로 지목할 수 없는 요소를 접는다.
/// 남길지 여부의 기준은 "에이전트가 id·name·text 로 지목할 수 있는가" 다 — 타입 목록을 두지 않으므로
/// DevExpress·yFiles 같은 서드파티 컨트롤에도 그대로 통하고, 유지할 목록도 없다.
/// 접힌 컨테이너 아래의 지목 가능한 요소는 가장 가까운 남은 조상 밑으로 올린다: 트리는 얕아지지만 빠지는 요소는 없다.
/// </summary>
internal static class SnapshotFilter
{
    #region Public Methods

    /// <summary>
    /// 지목 가능한 요소만 남긴 새 트리를 만듭니다. 원본은 바뀌지 않는다.
    /// </summary>
    /// <param name="root">원본 트리의 루트. 지목할 것이 없어도 루트는 남긴다 — 트리에 기준점이 필요하다.</param>
    /// <returns>걸러진 트리와, 접어 버린 노드 수.</returns>
    public static (ElementSnapshot Root, int Folded) KeepAddressable(ElementSnapshot root)
    {
        var folded = 0;
        var kept = CopyWithoutChildren(root);
        CollectInto(kept, root.Children, ref folded);
        return (kept, folded);
    }

    #endregion

    #region Private Methods

    /// <summary>자식들을 훑어 남길 것은 복사해 붙이고, 접을 것은 세고서 그 자식들을 같은 부모에 올립니다.</summary>
    private static void CollectInto(ElementSnapshot keptParent, List<ElementSnapshot> children, ref int folded)
    {
        foreach (var child in children)
        {
            if (IsAddressable(child))
            {
                var copy = CopyWithoutChildren(child);
                keptParent.Children.Add(copy);
                CollectInto(copy, child.Children, ref folded);
            }
            else
            {
                folded++;
                CollectInto(keptParent, child.Children, ref folded);
            }
        }
    }

    /// <summary>id·name·text 중 하나라도 있으면 셀렉터로 지목할 수 있다.</summary>
    private static bool IsAddressable(ElementSnapshot element)
    {
        return !string.IsNullOrWhiteSpace(element.AutomationId)
            || !string.IsNullOrWhiteSpace(element.Name)
            || !string.IsNullOrWhiteSpace(element.Text);
    }

    /// <summary>자식을 뺀 나머지 값을 그대로 옮긴 복사본을 만듭니다. ref 는 원본 그대로여야 조작에 쓸 수 있다.</summary>
    private static ElementSnapshot CopyWithoutChildren(ElementSnapshot element)
    {
        return new ElementSnapshot
        {
            Ref = element.Ref,
            Type = element.Type,
            Name = element.Name,
            AutomationId = element.AutomationId,
            Text = element.Text,
            IsEnabled = element.IsEnabled,
            IsVisible = element.IsVisible,
            Bounds = element.Bounds
        };
    }

    #endregion
}
