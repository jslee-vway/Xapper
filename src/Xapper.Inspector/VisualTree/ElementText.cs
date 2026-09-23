using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 요소가 담고 있는 텍스트를 읽는 규칙을 한곳에 모은 헬퍼.
/// 스냅샷·검색·좌표 조회가 같은 것을 텍스트로 보아야 결과를 견줄 수 있다.
/// </summary>
internal static class ElementText
{
    #region Fields

    /// <summary>
    /// 타입마다 찾아 둔 Text 속성. 한 화면에 같은 타입이 수백 개씩 나오므로 매번 반사로 뒤지면 순회가 느려진다.
    /// 찾지 못한 타입도 null 로 남겨 두 번 뒤지지 않는다.
    /// </summary>
    private static readonly Dictionary<Type, PropertyInfo?> TextProperties = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// 요소의 텍스트를 반환합니다. 텍스트를 갖지 않는 요소는 null.
    /// </summary>
    /// <param name="element">읽을 요소.</param>
    public static string? Of(DependencyObject element)
    {
        var known = element switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            ContentControl content when content.Content is string text => text,
            _ => null
        };

        return known ?? TextPropertyOf(element);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 표준 타입이 아닌 컨트롤에서 Text 속성을 읽습니다.
    ///
    /// 상용 컨트롤 묶음은 편집기와 콤보를 저마다의 타입으로 만들면서 값은 Text 로 내놓는 관례를 따른다.
    /// 표준 세 타입만 보면 그런 화면에서는 텍스트가 통째로 비어, 검색도 안 되고 기록에 지금 값도 남지 않는다
    /// (실측: 화면에 보이는 글자로 find 한 것이 다섯 번 넘게 빈손으로 돌아왔다).
    ///
    /// 타입을 늘어놓는 대신 속성의 생김새로 판단한다. 읽을 수 있는 string 속성이고 인자를 받지 않으면 그것을 쓴다.
    /// 예외를 던지지 않는다. 대상 앱 안에서 도는 코드이므로(결함 2 원칙) 읽을 수 없으면 null 로 둔다.
    /// </summary>
    private static string? TextPropertyOf(DependencyObject element)
    {
        try
        {
            var type = element.GetType();

            PropertyInfo? property;
            lock (TextProperties)
            {
                if (!TextProperties.TryGetValue(type, out property))
                {
                    property = type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
                    if (property is not null && (property.PropertyType != typeof(string)
                        || !property.CanRead || property.GetIndexParameters().Length > 0))
                    {
                        property = null;
                    }

                    TextProperties[type] = property;
                }
            }

            return property?.GetValue(element) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion
}
