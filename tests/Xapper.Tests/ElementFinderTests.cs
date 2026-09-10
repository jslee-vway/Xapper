using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xapper.Inspector.VisualTree;
using Xapper.Protocol.Messages.Requests;

namespace Xapper.Tests;

/// <summary>
/// 순회 불가 노드가 섞인 비주얼 트리를 만들어 주는 테스트 도구 모음.
/// yFiles GraphControl이나 DevExpress 컨트롤처럼 자식 슬롯을 직접 관리하는 컨트롤은
/// 자식 개수를 보고하면서도 아직 채워지지 않은 슬롯에 대해 아무것도 돌려주지 않을 수 있다.
/// </summary>
internal static class BrokenTree
{
    /// <summary>
    /// 자식이 하나 있다고 보고하지만 실제로는 비어 있는 슬롯을 돌려주는 컨트롤.
    /// </summary>
    private sealed class UnpopulatedChildSlot : FrameworkElement
    {
        private readonly Visual[] _slots = new Visual[1];

        protected override int VisualChildrenCount => _slots.Length;

        protected override Visual GetVisualChild(int index) => _slots[index];
    }

    /// <summary>
    /// 순회 불가 노드를 가운데 둔 트리를 만듭니다. 그 앞뒤 버튼은 모두 도달 가능해야 합니다.
    /// </summary>
    public static Canvas WithBrokenNodeInTheMiddle()
    {
        var root = new Canvas();
        root.Children.Add(new Button { Name = "firstTarget" });
        root.Children.Add(new UnpopulatedChildSlot());
        root.Children.Add(new Button { Name = "secondTarget" });
        return root;
    }
}

/// <summary>
/// 순회할 수 없는 노드를 만나도 검색이 트리 전체를 포기하지 않는지 검증하는 테스트 클래스.
/// </summary>
public class ElementFinderTests
{
    [Fact]
    public void Find_ByName_SkipsBrokenNodeAndReturnsAllMatches()
    {
        StaThread.Run(() =>
        {
            var result = new ElementFinder().Find(
                BrokenTree.WithBrokenNodeInTheMiddle(),
                new FindElementRequest { Name = "Target" },
                new RefRegistry());

            Assert.Equal(2, result.Matches.Count);
            Assert.Single(result.SkippedNodes);
            Assert.Contains("depth=1 child[0]", result.SkippedNodes[0]);
            Assert.Contains("UnpopulatedChildSlot", result.SkippedNodes[0]);
        });
    }

    [Fact]
    public void Find_ByType_SkipsBrokenNodeAndReturnsAllMatches()
    {
        StaThread.Run(() =>
        {
            var result = new ElementFinder().Find(
                BrokenTree.WithBrokenNodeInTheMiddle(),
                new FindElementRequest { Type = "Button" },
                new RefRegistry());

            Assert.Equal(2, result.Matches.Count);
        });
    }
}

/// <summary>
/// 스냅샷 순회가 같은 결함을 공유하지 않는지 검증하는 테스트 클래스.
/// 검색과 달리 깊이 제한이 있어 얕은 스냅샷에서는 문제가 드러나지 않는다.
/// </summary>
public class TreeWalkerTests
{
    [Fact]
    public void Walk_DeepEnoughToReachBrokenNode_StillProducesSnapshot()
    {
        StaThread.Run(() =>
        {
            var skipped = new List<string>();

            var snapshot = new TreeWalker().Walk(
                BrokenTree.WithBrokenNodeInTheMiddle(), new RefRegistry(), maxDepth: 5, skipped);

            Assert.Equal(3, snapshot.Children.Count);
            Assert.Single(skipped);
        });
    }
}
