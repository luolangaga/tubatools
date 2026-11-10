using System;
using System.Collections.Generic;

namespace TubaWinUi3.Tui.Services
{
    /// <summary>
    /// WMI 查询结果中的一行（属性名 → 属性值）。
    /// 值可能是 string、int、long、bool、ushort[] 等；取不到时返回 null。
    /// </summary>
    public sealed class WmiRow
    {
        private readonly Dictionary<string, object> _values;

        public WmiRow(Dictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public object this[string propertyName]
        {
            get
            {
                object value;
                return propertyName != null && _values.TryGetValue(propertyName, out value) ? value : null;
            }
        }
    }
}
