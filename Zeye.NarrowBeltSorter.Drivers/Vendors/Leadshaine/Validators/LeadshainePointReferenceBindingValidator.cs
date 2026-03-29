namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Validators {
    /// <summary>
    /// Leadshaine 点位引用通用校验工具。
    /// </summary>
    public static class LeadshainePointReferenceBindingValidator {
        /// <summary>
        /// 校验绑定配置是否引用有效点位。
        /// </summary>
        /// <typeparam name="TBinding">绑定配置类型。</typeparam>
        /// <param name="bindings">绑定配置集合。</param>
        /// <param name="pointIds">有效点位标识集合。</param>
        /// <param name="nameSelector">名称字段提取器。</param>
        /// <param name="pointIdSelector">PointId 字段提取器。</param>
        /// <param name="pathFactory">错误路径构造器。</param>
        /// <param name="nameField">名称字段名。</param>
        /// <returns>校验错误集合。</returns>
        public static IReadOnlyList<string> Validate<TBinding>(
            IReadOnlyList<TBinding> bindings,
            IReadOnlySet<string> pointIds,
            Func<TBinding, string?> nameSelector,
            Func<TBinding, string?> pointIdSelector,
            Func<int, string> pathFactory,
            string nameField) {
            var validationErrors = new List<string>(4);

            // 步骤1：逐项校验名称与 PointId 引用完整性。
            for (var i = 0; i < bindings.Count; i++) {
                var binding = bindings[i];
                var bindingPath = pathFactory(i);
                var name = nameSelector(binding);
                if (string.IsNullOrWhiteSpace(name)) {
                    validationErrors.Add($"{bindingPath}.{nameField} 不能为空。");
                }

                var pointId = pointIdSelector(binding);
                if (string.IsNullOrWhiteSpace(pointId)) {
                    validationErrors.Add($"{bindingPath}.PointId 不能为空。");
                    continue;
                }

                if (!pointIds.Contains(pointId)) {
                    validationErrors.Add($"{bindingPath}.PointId={pointId} 未在 Leadshaine.PointBindings.Points 中定义。");
                }
            }

            return validationErrors;
        }
    }
}
