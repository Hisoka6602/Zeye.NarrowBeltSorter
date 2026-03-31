using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Zeye.NarrowBeltSorter.Core.Models.Parcel;

namespace Zeye.NarrowBeltSorter.Execution.Parcel {

    /// <summary>
    /// 包裹信息只读视图，直接代理 ConcurrentDictionary 的 Values 枚举器，零拷贝。
    /// </summary>
    internal sealed class ParcelInfoReadOnlyView(ConcurrentDictionary<long, ParcelInfo> source) : IReadOnlyCollection<ParcelInfo> {
        private readonly ConcurrentDictionary<long, ParcelInfo> _source = source ?? throw new ArgumentNullException(nameof(source));

        /// <inheritdoc />
        public int Count => _source.Count;

        /// <inheritdoc />
        public IEnumerator<ParcelInfo> GetEnumerator() {
            return _source.Values.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}
