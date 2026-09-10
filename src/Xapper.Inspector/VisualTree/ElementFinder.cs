using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 비주얼 트리를 깊이 우선 탐색하여 조건에 매칭되는 UI 요소를 검색하는 클래스.
/// 타입은 클래스명 완전 일치, 이름·AutomationId·텍스트는 부분 일치이며 모두 대소문자를 무시.
/// 조건을 여러 개 주면 모두 만족하는 요소만 매칭.
/// </summary>
public sealed class ElementFinder
{
    /// <summary>
    /// 루트 요소부터 비주얼 트리를 탐색하여 조건에 맞는 요소를 검색합니다.
    /// 매칭된 요소는 RefRegistry에 등록되어 이후 액션에서 참조 가능.
    /// 순회할 수 없는 노드를 만나면 그 가지만 건너뛰고 나머지 탐색을 계속하며, 건너뛴 노드는 응답에 함께 보고.
    /// </summary>
    /// <param name="root">탐색 시작 지점.</param>
    /// <param name="request">검색 조건 (Name, AutomationId, Type, Text).</param>
    /// <param name="registry">매칭된 요소를 등록할 참조 레지스트리.</param>
    /// <returns>매칭된 요소 목록과 건너뛴 노드 목록.</returns>
    public FindElementResponse Find(DependencyObject root, FindElementRequest request, RefRegistry registry)
    {
        var response = new FindElementResponse();
        SearchTree(root, request, registry, response, depth: 0);
        return response;
    }

    #region Private Methods

    private void SearchTree(DependencyObject element, FindElementRequest request, RefRegistry registry, FindElementResponse response, int depth)
    {
        if (Matches(element, request))
        {
            var refId = registry.Register(element);
            response.Matches.Add(new ElementMatch
            {
                Ref = refId,
                Type = element.GetType().Name,
                Name = (element as FrameworkElement)?.Name,
                AutomationId = AutomationProperties.GetAutomationId(element),
                Text = GetText(element)
            });
        }

        foreach (var child in VisualChildren.Of(element, depth, response.SkippedNodes))
        {
            SearchTree(child, request, registry, response, depth + 1);
        }
    }

    private static bool Matches(DependencyObject element, FindElementRequest request)
    {
        if (request.Type != null)
        {
            if (!element.GetType().Name.Equals(request.Type, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (request.Name != null)
        {
            var name = (element as FrameworkElement)?.Name;
            if (name == null || !name.Contains(request.Name, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (request.AutomationId != null)
        {
            var id = AutomationProperties.GetAutomationId(element);
            if (id == null || !id.Contains(request.AutomationId, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (request.Text != null)
        {
            var text = GetText(element);
            if (text == null || !text.Contains(request.Text, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string? GetText(DependencyObject element)
    {
        return element switch
        {
            TextBlock tb => tb.Text,
            TextBox tb => tb.Text,
            ContentControl cc when cc.Content is string s => s,
            _ => null
        };
    }

    #endregion
}
