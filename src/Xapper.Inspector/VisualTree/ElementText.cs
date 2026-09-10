using System.Windows;
using System.Windows.Controls;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 요소가 담고 있는 텍스트를 읽는 규칙을 한곳에 모은 헬퍼.
/// 스냅샷·검색·좌표 조회가 같은 것을 텍스트로 보아야 결과를 견줄 수 있다.
/// </summary>
internal static class ElementText
{
    /// <summary>
    /// 요소의 텍스트를 반환합니다. 텍스트를 갖지 않는 요소는 null.
    /// </summary>
    /// <param name="element">읽을 요소.</param>
    public static string? Of(DependencyObject element)
    {
        return element switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            ContentControl content when content.Content is string text => text,
            _ => null
        };
    }
}
