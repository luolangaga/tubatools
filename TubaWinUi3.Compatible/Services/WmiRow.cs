using System;
using System.Collections.Generic;

namespace TubaWinUi3.Compatible.Services
{
    /// <summary>
    /// WMI 查询结果中的一行（属性名 → 属性值）。
    /// 值可能是 string、int、long、bool、ushort[] 等；取不到时返回 null。
    /// 两个兼容版共用的轻量替身，用来隔离 System.Management（PE 版走 AOT，见 PECompatible 的 WmiProvider）。
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
