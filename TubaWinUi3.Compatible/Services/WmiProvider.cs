using System;
using System.Collections.Generic;
using System.Management;

namespace TubaWinUi3.Compatible.Services
{
    /// <summary>
    /// .NET Framework 兼容版的 WMI 查询实现（System.Management）。
    /// PE 兼容版有同名同签名的 AOT 实现，见 TubaWinUi3.PECompatible/Services/WmiProvider.cs。
    /// </summary>
    public static class WmiProvider
    {
        public const string DefaultNamespace = @"root\cimv2";

        public static IReadOnlyList<WmiRow> Query(string wql)
        {
            return Query(DefaultNamespace, wql);
        }

        public static IReadOnlyList<WmiRow> Query(string ns, string wql)
        {
            var rows = new List<WmiRow>();
            if (string.IsNullOrWhiteSpace(wql))
                return rows;

            try
            {
                using (var searcher = new ManagementObjectSearcher(ns, wql))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        using (item)
                        {
                            var row = ToRow(item);
                            if (row != null) rows.Add(row);
                        }
                    }
                }
            }
            catch
            {
            }

            return rows;
        }

        private static WmiRow ToRow(ManagementBaseObject item)
        {
            if (item == null) return null;

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (PropertyData property in item.Properties)
                {
                    try { values[property.Name] = property.Value; }
                    catch { }
                }
            }
            catch
            {
                return null;
            }

            return new WmiRow(values);
        }
    }
}
