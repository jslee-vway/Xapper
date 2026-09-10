using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Inspector.Diagnostics;

/// <summary>
/// UI 요소의 프로퍼티 값을 동적으로 읽는 유틸리티 클래스.
/// DependencyProperty를 우선 탐색하고, 없으면 CLR 프로퍼티를 리플렉션으로 조회.
/// </summary>
public static class PropertyReader
{
    /// <summary>
    /// 지정된 UI 요소에서 프로퍼티 값을 읽습니다.
    /// </summary>
    /// <param name="element">대상 요소.</param>
    /// <param name="propertyName">읽을 프로퍼티 이름.</param>
    /// <returns>프로퍼티 이름, 값, 타입이 포함된 응답. Ref는 호출자가 설정해야 함.</returns>
    /// <exception cref="InvalidOperationException">프로퍼티를 찾을 수 없는 경우.</exception>
    public static PropertyResponse ReadProperty(DependencyObject element, string propertyName)
    {
        // 점이 들어간 이름은 속성 경로로 쓴 것인데 이 도구는 경로를 해석하지 않는다.
        // 그냥 못 찾았다고 하면 "그 속성이 없다"로 읽혀 원인을 엉뚱한 곳에서 찾게 된다.
        if (propertyName.Contains('.'))
            throw new InvalidOperationException(
                $"Property paths are not supported: \"{propertyName}\". Pass one property name that the " +
                "element itself declares, and read its value from the result. Attached properties written " +
                "as Owner.Name cannot be read this way either.");

        // DependencyProperty 우선 탐색 (정적 필드 "{Name}Property" 패턴)
        var dpField = element.GetType()
            .GetField($"{propertyName}Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (dpField?.GetValue(null) is DependencyProperty dp)
        {
            var value = element.GetValue(dp);
            return new PropertyResponse
            {
                Ref = 0,
                PropertyName = propertyName,
                Value = value?.ToString(),
                ValueType = value?.GetType().Name ?? "null"
            };
        }

        // CLR 프로퍼티 리플렉션 폴백
        var prop = element.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null)
        {
            var value = prop.GetValue(element);
            return new PropertyResponse
            {
                Ref = 0,
                PropertyName = propertyName,
                Value = value?.ToString(),
                ValueType = value?.GetType().Name ?? "null"
            };
        }

        throw new InvalidOperationException(
            $"Property \"{propertyName}\" not found on {element.GetType().Name}. " +
            "The name must match a dependency property or a public instance property of that exact type. " +
            "Use xapper_snapshot to confirm the element type first.");
    }
}
