using Zeye.NarrowBeltSorter.Core.Manager.Emc;
using csLTDMC;

namespace Zeye.NarrowBeltSorter.Drivers.Vendors.Leadshaine.Emc {
    /// <summary>
    /// Leadshaine EMC 硬件访问适配器。
    /// </summary>
    public sealed class LeadshaineEmcHardwareAdapter : IEmcHardwareAdapter {
        /// <inheritdoc />
        public short InitializeBoard(ushort cardNo, string? controllerIp) {
            return string.IsNullOrWhiteSpace(controllerIp)
                ? LTDMC.dmc_board_init()
                : LTDMC.dmc_board_init_eth(cardNo, controllerIp);
        }

        /// <inheritdoc />
        public short SoftReset(ushort cardNo) {
            return LTDMC.dmc_soft_reset(cardNo);
        }

        /// <inheritdoc />
        public short GetErrorCode(ushort cardNo, ushort channel, ref ushort errorCode) {
            return LTDMC.nmc_get_errcode(cardNo, channel, ref errorCode);
        }

        /// <inheritdoc />
        public uint ReadInPort(ushort cardNo, ushort portNo) {
            return LTDMC.dmc_read_inport(cardNo, portNo);
        }

        /// <inheritdoc />
        public short WriteOutBit(ushort cardNo, ushort bitNo, ushort onOff) {
            return LTDMC.dmc_write_outbit(cardNo, bitNo, onOff);
        }

        /// <inheritdoc />
        public short CloseBoard() {
            return LTDMC.dmc_board_close();
        }
    }
}
