namespace Zeye.NarrowBeltSorter.Core.Utilities.Chutes {
    public static class ZhiQianAddressMap {
        public const int DoIndexMin = 1;
        public const int DoIndexMax = 32;
        public const int DoChannelCount = 32;

        public static bool ValidateDoIndex(int doIndex) => doIndex >= DoIndexMin && doIndex <= DoIndexMax;
    }
}
