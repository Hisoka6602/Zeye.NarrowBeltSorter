using Microsoft.Extensions.Options;

namespace Zeye.NarrowBeltSorter.Host.Vendors.DependencyInjection {
    /// <summary>
    /// Leadshaine 配置委托校验器。
    /// </summary>
    /// <typeparam name="TOptions">配置类型。</typeparam>
    public sealed class LeadshaineOptionsDelegateValidator<TOptions> : IValidateOptions<TOptions> where TOptions : class {
        private readonly string? _name;
        private readonly Func<TOptions, IReadOnlyList<string>> _validator;

        /// <summary>
        /// 初始化校验器实例。
        /// </summary>
        /// <param name="name">配置名称。</param>
        /// <param name="validator">校验委托。</param>
        public LeadshaineOptionsDelegateValidator(
            string? name,
            Func<TOptions, IReadOnlyList<string>> validator) {
            _name = name;
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>
        /// 执行配置校验。
        /// </summary>
        /// <param name="name">配置名称。</param>
        /// <param name="options">配置对象。</param>
        /// <returns>校验结果。</returns>
        public ValidateOptionsResult Validate(string? name, TOptions options) {
            if (_name is not null && !string.Equals(_name, name, StringComparison.Ordinal)) {
                return ValidateOptionsResult.Skip;
            }

            var errors = _validator(options);
            if (errors.Count == 0) {
                return ValidateOptionsResult.Success;
            }

            return ValidateOptionsResult.Fail(errors);
        }
    }
}
