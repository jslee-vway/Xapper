using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Xapper.Inspector.Actions;

/// <summary>
/// ScrollViewer 내에서 스크롤 위치를 변경하는 정적 클래스.
/// AutomationPeer(IScrollProvider) → ScrollViewer 직접 조작 순으로 시도.
/// </summary>
public static class ScrollAction
{
    /// <summary>
    /// 지정된 UI 요소의 스크롤 위치를 변경합니다.
    /// 스크롤을 지원하지 않는 요소는 예외 대신 실패 결과로 알린다(결함 2).
    /// </summary>
    /// <param name="element">대상 ScrollViewer 요소.</param>
    /// <param name="horizontalPercent">수평 스크롤 퍼센트 (0~100). -1이면 변경 안 함.</param>
    /// <param name="verticalPercent">수직 스크롤 퍼센트 (0~100). -1이면 변경 안 함.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult Execute(UIElement element, double horizontalPercent, double verticalPercent)
    {
        // Priority 1: AutomationPeer IScrollProvider
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Scroll) is IScrollProvider scroller)
        {
            scroller.SetScrollPercent(horizontalPercent, verticalPercent);
            return ActionResult.Ok;
        }

        // Priority 2: Direct ScrollViewer
        if (element is ScrollViewer scrollViewer)
        {
            if (horizontalPercent >= 0)
                scrollViewer.ScrollToHorizontalOffset(
                    scrollViewer.ScrollableWidth * horizontalPercent / 100.0);
            if (verticalPercent >= 0)
                scrollViewer.ScrollToVerticalOffset(
                    scrollViewer.ScrollableHeight * verticalPercent / 100.0);
            return ActionResult.Ok;
        }

        return ActionResult.Fail($"Element {element.GetType().Name} does not support scrolling");
    }
}
